using Comuki.Modules.Work.Application.Admission;
using Comuki.Modules.Work.Application.Ports.Engine;
using Comuki.Modules.Work.Application.Ports.InboundItems;
using Comuki.Modules.Work.Domain.Exceptions;
using Comuki.Modules.Work.Domain.Sources;
using Comuki.Modules.Work.Infrastructure.Options;
using Comuki.Shared.Bootstrap.Workers;
using Comuki.Shared.Contracts.Integrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comuki.Modules.Work.Infrastructure.Subscribers;

/// <summary>
/// Host-composed <see cref="WorkSubscriberBase"/> that polls the
/// <c>orchestration.outbox_messages</c> table for the
/// <c>integration.inbound.admitted.v1</c> event the Integrations
/// module publishes from <c>ClaimInboundItemHandler</c> (task 3.4
/// of <c>add-work-management</c>). On each row the subscriber
/// loads the corresponding <see cref="InboundItemSnapshot"/>
/// from the Integrations store (closed via
/// <see cref="IInboundItemReader"/>), translates the provider
/// wire form into <see cref="WorkTaskSourceKind"/>, then calls
/// <see cref="AdmitTaskHandler"/> with the right command shape.
/// <para>
/// The subscriber is the umbrella's Phase A boundary — gated on
/// <see cref="WorkOptions.AdmissionEnabled"/>. Off by
/// default in prod until Phase A ships; the gate is the
/// "standalone Tasks" cutover marker per the design's runtime
/// gate table.
/// </para>
/// <para>
/// Dedupe lives in two layers: the per-type watermark advances
/// only on the highest <c>id</c> observed so a host restart
/// resumes from the last-seen position; the Work-side
/// <see cref="AdmitTaskHandler.InboxMessageId"/> key
/// (<c>work.inbox.admit.{externalId}</c>) is the per-inbound
/// dedupe the handler itself claims via <see cref="Application.Ports.Inbox.IWorkInbox"/>.
/// A re-poll of the same engine outbox row yields a no-op from
/// the handler (replay path).
/// </para>
/// </summary>
/// <param name="workOptions">The feature flags — the <c>AdmissionEnabled</c> gate.</param>
public sealed class WorkAdmissionSubscriber(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    IOptions<WorkOptions> workOptions,
    ILogger<WorkAdmissionSubscriber> logger)
    : WorkSubscriberBase(scopeFactory, clock, logger)
{
    /// <inheritdoc />
    public override string Name => "work-admission-subscriber";

    /// <inheritdoc />
    protected override IReadOnlyCollection<string> GetSubscribedTypes()
    {
        return [SubscribedType];
    }

    /// <inheritdoc />
    protected override string WatermarkKey => SubscribedType;

    /// <inheritdoc />
    public override async Task<WorkerResult> ExecuteAsync(WorkerContext context, CancellationToken cancellationToken)
    {
        // Phase A gate — the runtime feature flag. Off in prod
        // until the umbrella's standalone-Tasks boundary flips.
        // The worker still ticks (so a delayed enable is picked
        // up on the next cycle), but it short-circuits the heavy
        // work.
        return !workOptions.Value.AdmissionEnabled
            ? WorkerResult.Ok("admission-disabled")
            : await base.ExecuteAsync(context, cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task<bool> HandleRowAsync(
        OrchestrationOutboxRow row,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        var admittedEvent = TryReadAdmitted(row.Payload);
        if (admittedEvent is null)
        {
            return false;
        }

        var reader = serviceProvider.GetRequiredService<IInboundItemReader>();
        var handler = serviceProvider.GetRequiredService<AdmitTaskHandler>();

        var snapshot = await reader.FindAsync(admittedEvent.InboundItemId, cancellationToken);
        if (snapshot is null)
        {
            // The inbound item was deleted between the publisher's
            // claim and our poll. The watermark still advances so
            // the cycle doesn't loop.
            Logger.LogDebug(
                "WorkAdmissionSubscriber skipping row {RowId}: inbound {InboundId} was deleted between claim and poll",
                row.Id, admittedEvent.InboundItemId);
            return false;
        }

        if (!WorkTaskSourceKind.TryFromWire(snapshot.ProviderWire, out var sourceKind))
        {
            // Unknown provider wire form — log + skip. The
            // watermark still advances so a poison row doesn't
            // block the cycle.
            Logger.LogWarning(
                "WorkAdmissionSubscriber encountered unknown provider wire {Provider} for inbound {InboundId}",
                snapshot.ProviderWire, admittedEvent.InboundItemId);
            return false;
        }

        if (!Guid.TryParse(admittedEvent.ProjectId, out var projectGuid))
        {
            Logger.LogWarning(
                "WorkAdmissionSubscriber encountered malformed project id {ProjectId} for inbound {InboundId}",
                admittedEvent.ProjectId, admittedEvent.InboundItemId);
            return false;
        }

        try
        {
            await handler.HandleAsync(
                new AdmitTaskCommand(
                    InboundItemExternalId: snapshot.ExternalId,
                    ProjectId: new Shared.Kernel.Ids.ProjectId(projectGuid),
                    Title: snapshot.Title,
                    Brief: snapshot.Body,
                    SourceKind: sourceKind,
                    SourceDisplayName: snapshot.ProviderWire),
                cancellationToken);
            return true;
        }
        catch (WorkTaskDomainException exception)
        {
            // The admit handler throws WorkTaskDomainException for
            // invariant violations (e.g. the inbox was claimed but
            // the binding row is missing — data inconsistency). We
            // log + skip + advance the watermark so the cycle
            // doesn't loop on the poison row.
            Logger.LogError(
                exception,
                "WorkAdmissionSubscriber failed to admit inbound {InboundId}",
                admittedEvent.InboundItemId);
            return false;
        }
    }

    /// <summary>The engine-emitted event the Work-side admission subscriber reads.</summary>
    private const string SubscribedType = IntegrationWireEventTypes.InboundAdmittedV1;

    /// <summary>Best-effort parse of the integration.inbound.admitted.v1 payload — null on malformed.</summary>
    private static IntegrationInboundAdmittedEvent? TryReadAdmitted(string payload)
    {
        return WorkTaskEventJson.TryDeserialize<IntegrationInboundAdmittedEvent>(payload);
    }

}
