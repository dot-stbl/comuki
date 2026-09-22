namespace Comuki.Engine.Orchestration.Application.MergeQueue.Abandon;

/// <summary>
/// Application-layer command: mark a merge-queue entry as abandoned
/// (Pending or InProgress). Reason is required and bounded; the domain
/// factory re-enforces non-emptiness.
/// </summary>
/// <param name="EntryId">Target entry.</param>
/// <param name="Reason">Operator-supplied reason; non-empty, bounded.</param>
public sealed record AbandonMergeQueueCommand(
    Guid EntryId,
    string Reason);
