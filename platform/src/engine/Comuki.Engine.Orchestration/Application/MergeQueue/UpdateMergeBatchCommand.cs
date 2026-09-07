namespace Comuki.Engine.Orchestration.Application.MergeQueue;

/// <summary>Application-layer command: transition one merge batch by id.</summary>
/// <param name="BatchId">Target batch.</param>
/// <param name="Action">What to do.</param>
/// <param name="Reason">Abandon reason; only used by <see cref="MergeBatchAction.Abandon"/>.</param>
public sealed record UpdateMergeBatchCommand(
    Guid BatchId,
    MergeBatchAction Action,
    string? Reason);

/// <summary>Actions exposed by the merge-batch service.</summary>
public enum MergeBatchAction
{
    /// <summary>Move pending batch to in-progress (operator picked up the first entry).</summary>
    Claim = 0,

    /// <summary>Mark in-progress batch as merged (every entry has landed).</summary>
    Merge = 1,

    /// <summary>Mark batch as abandoned (reason required).</summary>
    Abandon = 2,
}
