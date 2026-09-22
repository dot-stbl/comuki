namespace Comuki.Engine.Orchestration.Application.MergeQueue.Annotate;

/// <summary>
/// Application-layer command: update free-text notes on a merge-queue
/// entry without changing its status. Any status accepts an annotation.
/// </summary>
/// <param name="EntryId">Target entry.</param>
/// <param name="Notes">Free-text notes; bounded.</param>
public sealed record AnnotateMergeQueueCommand(
    Guid EntryId,
    string? Notes);
