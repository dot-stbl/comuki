namespace Comuki.Modules.Work.Application.Ports.Integrations;

/// <summary>
/// Work-side port the host wires to the Integrations module's
/// outbound sync path. The Work bounded context never references
/// <c>Comuki.Modules.Integrations</c> directly — this port lives in
/// the Work Application layer as the only seam the
/// <c>WorkSyncBridgeComukiWorker</c> (and any future Work-driven
/// integrations outbound) holds. The host closes it over the
/// Integrations store (<c>EnqueueSyncJobAsync</c>) in a thin
/// adapter; the Inputs/Outputs are deliberately narrow so adding
/// a new Work-emitted integrations event does not require
/// editing the Integrations store contract.
/// </summary>
public interface IIntegrationsSyncPort
{
    /// <summary>
    /// Enqueues an outbound sync job for a Work-side terminal
    /// event. The host-adapter implementation translates the
    /// primitive inputs into the Integrations'
    /// <c>SyncJob.Create</c> shape — the Work module never sees
    /// the Integrations domain types.
    /// </summary>
    /// <param name="taskId">The Work <c>TaskId</c> as a <see cref="Guid"/>; the adapter uses it as the canonical linkage for downstream comment threads.</param>
    /// <param name="primarySourceExternalId">
    /// The primary source ref's external id when one exists (the
    /// tracker ticket's id); <c>null</c> for native Tasks with no
    /// primary source. The adapter uses it as both the inbound
    /// id (when parseable as a Guid) and the ticket external id
    /// so the downstream comment routes back to the same ticket
    /// the Work admitted.
    /// </param>
    /// <param name="statusForJob">
    /// <c>"Succeeded"</c> / <c>"Waived"</c> / <c>"Replaced"</c> /
    /// <c>"Failed"</c> on Resolved, <c>"Cancelled"</c> on the
    /// terminal Cancelled path.
    /// </param>
    /// <param name="occurredAt">Wall-clock stamp the adapter carries into <c>SyncJob</c>.</param>
    /// <param name="cancellationToken">Cancellation token; cancelled enqueue surface as <see cref="OperationCanceledException"/> (the worker treats the job as not-yet-enqueued and the next cycle re-emits it from the Task's outbox).</param>
    /// <returns>A task that completes when the sync job has been enqueued; the caller's outbox row is then committed together with the work-side update in the same transaction.</returns>
    public Task EnqueueTerminalSyncAsync(
        Guid taskId,
        string? primarySourceExternalId,
        string statusForJob,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default);
}
