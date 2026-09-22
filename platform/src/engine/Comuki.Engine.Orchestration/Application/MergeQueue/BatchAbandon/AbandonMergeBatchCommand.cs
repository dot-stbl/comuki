namespace Comuki.Engine.Orchestration.Application.MergeQueue.BatchAbandon;

/// <summary>
/// Application-layer command: mark a merge batch as abandoned
/// (Pending or InProgress). Reason is required and bounded; the
/// domain factory re-enforces non-emptiness.
/// </summary>
/// <param name="BatchId">Target batch.</param>
/// <param name="Reason">Operator-supplied reason; non-empty, bounded.</param>
public sealed record AbandonMergeBatchCommand(
    Guid BatchId,
    string Reason);
