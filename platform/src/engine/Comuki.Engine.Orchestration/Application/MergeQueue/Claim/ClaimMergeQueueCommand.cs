namespace Comuki.Engine.Orchestration.Application.MergeQueue.Claim;

/// <summary>
/// Application-layer command: claim one merge-queue entry for an
/// operator. Validated structurally (<see cref="ClaimMergeQueueValidator"/>);
/// the domain factory re-enforces the same invariants at the store.
/// </summary>
/// <param name="EntryId">Target entry.</param>
/// <param name="OperatorId">Operator id; non-empty, bounded.</param>
public sealed record ClaimMergeQueueCommand(
    Guid EntryId,
    string OperatorId);
