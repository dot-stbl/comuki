namespace Comuki.Engine.Orchestration.Application.MergeQueue.Release;

/// <summary>
/// Application-layer command: release an in-progress merge-queue entry
/// claim back to pending. No payload beyond the target id — the
/// operator is whoever currently holds the claim, recorded on the row.
/// </summary>
/// <param name="EntryId">Target entry.</param>
public sealed record ReleaseMergeQueueCommand(Guid EntryId);
