namespace Comuki.Engine.Orchestration.Application.MergeQueue.MergeEntry;

/// <summary>
/// Application-layer command: mark an in-progress merge-queue entry as
/// merged. The transition is timestamped server-side — no operator
/// payload required.
/// </summary>
/// <param name="EntryId">Target entry.</param>
public sealed record MergeMergeQueueCommand(Guid EntryId);
