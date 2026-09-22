namespace Comuki.Engine.Orchestration.Application.MergeQueue.BatchMerge;

/// <summary>
/// Application-layer command: mark an in-progress merge batch as
/// merged. The transition is timestamped server-side.
/// </summary>
/// <param name="BatchId">Target batch.</param>
public sealed record MergeMergeBatchCommand(Guid BatchId);
