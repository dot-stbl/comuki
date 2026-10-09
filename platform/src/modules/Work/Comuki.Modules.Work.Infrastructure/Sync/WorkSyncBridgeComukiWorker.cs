using Comuki.Modules.Work.Application.Events;
using Comuki.Modules.Work.Application.Ports.Engine;
using Comuki.Modules.Work.Application.Ports.Inbox;
using Comuki.Modules.Work.Application.Ports.Integrations;
using Comuki.Modules.Work.Application.Ports.Persistence;
using Comuki.Modules.Work.Domain.Ids;
using Comuki.Modules.Work.Infrastructure.Subscribers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Comuki.Modules.Work.Infrastructure.Sync;

/// <summary>
/// The Work-resolution-to-Integrations sync-bridge
/// (<c>add-work-management</c> tasks 4.1 / 4.2 / 4.3). Polls the
/// Work outbox for the Work-emitted terminal events
/// <see cref="WorkTaskEventTypes.Resolved"/> and
/// <see cref="WorkTaskEventTypes.Cancelled"/>, and emits one
/// deduped outbound sync job per Task id through
/// <see cref="IIntegrationsSyncPort"/>. Per-attempt cancellation
/// (<see cref="WorkTaskEventTypes.AttemptCancelled"/>) is NOT
/// subscribed here on purpose — that wire envelope fires while
/// the Task is still alive (Retry path); the terminal
/// <see cref="WorkTaskEventTypes.Cancelled"/> only fires from the
/// <c>Work.Decide</c> <c>Cancellation</c> branch via
/// <c>CancellationHandler</c>. The two are distinct event types
/// rather than a payload discriminator so the engine-side cancel
/// port and the integrations outbound can subscribe independently.
/// <para>
/// The dedupe key is one ledger per
/// (<see cref="WorkTaskEventTypes.Resolved"/> /
/// <see cref="WorkTaskEventTypes.Cancelled"/>) task id — two
/// distinct ledgers, not a shared one (a Task that resolves once
/// gets exactly one Resolved sync job, even if its terminal
/// <see cref="WorkTaskEventTypes.Cancelled"/> also fires for the
/// same task id under a different version). The brief version on
/// the envelope is the only non-id monotonic counter, so the
/// key rides <c>work.task.{taskId}:{resolved|cancelled}:{version}</c>
/// — a superseding Resolve/Cancel re-emits under the same version
/// (no-op via the claim) and a brief edit advances the version
/// (lets the new sync job land).
/// </para>
/// <para>
/// Replaces the legacy <c>RunStatusBridgeComukiWorker</c>
/// subscription when the <c>work.management.sync.enabled</c>
/// flag flips on (task 4.3); the legacy worker stays active
/// while the flag is off and is removed when the flag flips on.
/// Gating is registered by the host in
/// <c>Comuki.Host.HostComposer</c>.
/// </para>
/// </summary>
public sealed class WorkSyncBridgeComukiWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<WorkSyncBridgeComukiWorker> logger)
    : WorkSubscriberBase(scopeFactory, clock, logger)
{
    /// <inheritdoc />
    public override string Name => "work-sync-bridge";

    /// <inheritdoc />
    protected override IReadOnlyCollection<string> GetSubscribedTypes()
    {
        return subscribedTypes;
    }

    /// <inheritdoc />
    protected override string WatermarkKey => WatermarkKeyInternal;

    /// <inheritdoc />
    protected override async Task<bool> HandleRowAsync(
        OrchestrationOutboxRow row,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        var payload = TryReadTaskEvent(row.Payload);
        if (payload is null)
        {
            return false;
        }

        // The dedupe key rides the brief version — the only non-id
        // monotonic counter on the envelope. Resolved + Cancelled
        // each get their own ledger (separate dedupe prefix), so a
        // Task with both terminal events under different versions
        // produces both sync jobs instead of having the second
        // claim collide with the first.
        var dedupeKey = BuildDedupeKey(row.Type, payload.TaskId, payload.BriefVersion ?? 0);
        var inbox = serviceProvider.GetRequiredService<IWorkInbox>();
        if (!await inbox.TryClaimAsync(dedupeKey, cancellationToken))
        {
            return false;
        }

        var workStore = serviceProvider.GetRequiredService<IWorkTaskStore>();
        var syncPort = serviceProvider.GetRequiredService<IIntegrationsSyncPort>();

        var task = await workStore.FindAsync(new WorkTaskId(payload.TaskId), cancellationToken);
        var primaryExternalId = task?.SourceRefs
            .Where(static source => source.IsPrimary)
            .Select(static source => source.ExternalId)
            .FirstOrDefault();
        var statusForJob = row.Type == WorkTaskEventTypes.Resolved
            ? (task?.ResolutionOutcome?.Value ?? "Succeeded")
            : "Cancelled";

        await syncPort.EnqueueTerminalSyncAsync(
            payload.TaskId,
            primaryExternalId,
            statusForJob,
            Clock.GetUtcNow(),
            cancellationToken);

        return true;
    }

    /// <summary>The terminal event types — distinct ledgers on the same watermark slot.</summary>
    private static readonly IReadOnlyCollection<string> subscribedTypes =
    [
        WorkTaskEventTypes.Resolved,
        WorkTaskEventTypes.Cancelled,
    ];

    /// <summary>The watermark key — one counter covers both subscribed types (they share the Work-side dedupe slot; the dedupe key prefix differentiates them).</summary>
    private const string WatermarkKeyInternal = "work.task.terminal.v1";

    private static string BuildDedupeKey(string type, Guid taskId, int briefVersion)
    {
        var suffix = type == WorkTaskEventTypes.Resolved ? "resolved" : "cancelled";
        return $"work.task.{taskId}:{suffix}:{briefVersion}";
    }

    private static WorkTaskEvent? TryReadTaskEvent(string payload)
    {
        return WorkTaskEventJson.TryDeserialize<WorkTaskEvent>(payload);
    }
}
