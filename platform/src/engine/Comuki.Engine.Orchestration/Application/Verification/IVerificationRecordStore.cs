using Comuki.Engine.Orchestration.Domain.Verification;

namespace Comuki.Engine.Orchestration.Application.Verification;

/// <summary>
/// Persistence port for the <c>verifications</c> table
/// (add-orchestra §3 — Coda, <c>verification/spec.md</c> Requirement
/// "VerificationRecord is a per-WorkItem sibling table"). Scoped —
/// one store per orchestration scope. The store owns the
/// <c>INSERT ... ON CONFLICT</c> upsert on
/// <c>(work_item_id, gate_name)</c>: a re-evaluation produces an
/// updated row, never a duplicate (Requirement "Per-gate uniqueness").
/// </summary>
public interface IVerificationRecordStore
{
    /// <summary>
    /// Inserts a fresh <see cref="VerificationRecord"/> or updates the
    /// existing row for the (work item, gate) pair. The upsert is
    /// idempotent — a second call with the same key overwrites the
    /// verdict / evidence / evaluator / timestamp and re-stamps the
    /// row's <c>id</c> only when no existing row was matched.
    /// </summary>
    /// <param name="record">The record to persist.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    public Task UpsertAsync(VerificationRecord record, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the records for one work item. Newest first by
    /// <c>evaluated_at</c>; empty when the work item has no verdicts
    /// yet.
    /// </summary>
    /// <param name="workItemId">Work item the gates verdicted.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    public Task<IReadOnlyList<VerificationRecord>> ListByWorkItemAsync(
        Guid workItemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the records for every work item under a run. Newest
    /// first per work item; used by the verification view to render
    /// the per-gate verdict list.
    /// </summary>
    /// <param name="workItemIds">Work item ids the run owns.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    public Task<IReadOnlyList<VerificationRecord>> ListByWorkItemsAsync(
        IReadOnlyList<Guid> workItemIds,
        CancellationToken cancellationToken = default);
}
