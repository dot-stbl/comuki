namespace Comuki.Engine.Orchestration.Application.MergeQueue;

/// <summary>Application-layer command: transition one merge-queue entry by id.</summary>
/// <param name="EntryId">Target entry.</param>
/// <param name="Action">What to do.</param>
/// <param name="OperatorId">Operator id (only used by Claim / Abandon-with-actor).</param>
/// <param name="Reason">Abandon reason; only used by <see cref="MergeQueueAction.Abandon"/>.</param>
/// <param name="Notes">Notes override; only used by <see cref="MergeQueueAction.Annotate"/>.</param>
public sealed record UpdateMergeQueueCommand(
    Guid EntryId,
    MergeQueueAction Action,
    string? OperatorId,
    string? Reason,
    string? Notes);

/// <summary>Actions exposed by the PATCH endpoint; one command shape, several behaviours.</summary>
public enum MergeQueueAction
{
    /// <summary>Operator takes ownership of a pending entry.</summary>
    Claim = 0,

    /// <summary>Release an in-progress claim back to pending.</summary>
    Release = 1,

    /// <summary>Mark in-progress entry as merged.</summary>
    Merge = 2,

    /// <summary>Mark the entry as abandoned (reason required).</summary>
    Abandon = 3,

    /// <summary>Update free-text notes on the entry (no status change).</summary>
    Annotate = 4,
}
