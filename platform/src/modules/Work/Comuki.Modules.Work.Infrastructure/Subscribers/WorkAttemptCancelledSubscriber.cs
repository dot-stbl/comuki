using Comuki.Modules.Work.Application.Events;
using Comuki.Modules.Work.Application.Ports.Engine;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static Comuki.Modules.Work.Infrastructure.Subscribers.WorkTaskEventJson;

namespace Comuki.Modules.Work.Infrastructure.Subscribers;

/// <summary>
/// Host-composed <see cref="WorkSubscriberBase"/> that polls the
/// Work outbox for <see cref="WorkTaskEventTypes.AttemptCancelled"/>
/// (published by <see cref="Cancellation.CancelAttemptHandler"/>;
/// the dedicated envelope carries <c>RunId</c> explicitly because
/// the Task's active attempt is cleared in the same transaction —
/// the broader <see cref="WorkTaskEvent"/> envelope would have its
/// <c>ActiveAttemptId</c> field null at the publish site and every
/// row would be skipped as poison). On each row the subscriber
/// forwards the Work-supplied <see cref="WorkAttemptCancelledEvent.RunId"/>
/// to <see cref="IWorkCancelPort"/> (closed in the host by
/// <c>HostCancelRunAdapter</c>); that adapter performs the
/// fence-and-cancel transaction. The Work outbox row's <c>id</c>
/// drives the per-type watermark in <see cref="WorkSubscriberBase"/>;
/// a retried cancel no-ops via the watermark's monotonic advance.
/// </summary>
public sealed class WorkAttemptCancelledSubscriber(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<WorkAttemptCancelledSubscriber> logger)
    : WorkSubscriberBase(scopeFactory, clock, logger)
{
    /// <inheritdoc />
    public override string Name => "work-attempt-cancelled-subscriber";

    /// <inheritdoc />
    protected override IReadOnlyCollection<string> GetSubscribedTypes()
    {
        return [SubscribedType];
    }

    /// <inheritdoc />
    protected override string WatermarkKey => SubscribedType;

    /// <inheritdoc />
    protected override async Task<bool> HandleRowAsync(
        OrchestrationOutboxRow row,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        var runId = TryReadRunId(row.Payload);
        if (runId is null)
        {
            return false;
        }

        var cancelPort = serviceProvider.GetRequiredService<IWorkCancelPort>();
        try
        {
            await cancelPort.CancelAsync(runId.Value, "work.attempt-cancelled", cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The cancel port is closed by the host (HostCancelRunAdapter)
            // which surfaces RunDecisionConflictException for an
            // already-terminal Run — the architecture forbids the Work
            // module referencing the host assembly directly, so we
            // catch broadly here (with the cooperation token
            // filtered out) and trust the watermark advance to skip the
            // already-processed row on the next cycle.
            Logger.LogWarning(
                exception,
                "WorkAttemptCancelledSubscriber failed to forward cancel for Run {RunId}",
                runId);
            return false;
        }
    }

    /// <summary>Subscribed Work-outbox type — the CancelAttempt source-of-truth.</summary>
    private const string SubscribedType = WorkTaskEventTypes.AttemptCancelled;

    private static RunId? TryReadRunId(string payload)
    {
        return TryDeserialize<WorkAttemptCancelledEvent>(payload) is { RunId: { } runId }
            ? new RunId(runId)
            : null;
    }
}
