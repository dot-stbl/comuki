namespace Comuki.Engine.Orchestration.Domain.MergeQueue;

/// <summary>
/// Lifecycle status of a single merge-queue entry. Mirrors the
/// <see cref="WorkItems.WorkItemStatus"/> shape but is independent — the
/// merge-queue is an operator-facing coordination tool, not a worker
/// lease, and its transitions are gated by the table in
/// <see cref="MergeQueueTransitions"/>.
/// </summary>
public enum MergeQueueStatus
{
    /// <summary>Newly enqueued; no operator has picked it up.</summary>
    Pending = 0,

    /// <summary>Operator has claimed the entry and is driving the merge.</summary>
    InProgress = 1,

    /// <summary>Merge completed; the entry is a historical record only.</summary>
    Merged = 2,

    /// <summary>Operator marked the entry as dropped (rejected, retracted, superseded).</summary>
    Abandoned = 3,
}