namespace Comuki.Engine.Orchestration.Domain.MergeQueue;

/// <summary>
/// Lifecycle status of a merge batch — a coordinated group of
/// merge-queue entries that share a release window. Mirrors the
/// <see cref="MergeQueueStatus"/> shape so a batch's status is the
/// aggregate signal of its entries: a batch is
/// <see cref="InProgress"/> once any entry is claimed and
/// <see cref="Merged"/> only after every entry has landed.
/// </summary>
public enum MergeBatchStatus
{
    /// <summary>Newly created; no entries have been claimed yet.</summary>
    Pending = 0,

    /// <summary>At least one entry has been claimed by an operator.</summary>
    InProgress = 1,

    /// <summary>Every entry in the batch has landed on its target.</summary>
    Merged = 2,

    /// <summary>Operator dropped the batch (e.g. release train cancelled).</summary>
    Abandoned = 3,
}
