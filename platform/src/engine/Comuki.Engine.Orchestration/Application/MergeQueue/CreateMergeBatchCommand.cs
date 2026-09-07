namespace Comuki.Engine.Orchestration.Application.MergeQueue;

/// <summary>Application-layer command: create a new merge batch.</summary>
/// <param name="Name">Operator-supplied human-readable batch name; non-empty.</param>
/// <param name="PullRequestUrls">Ordered PR URLs the batch ships; at least one, each non-empty.</param>
public sealed record CreateMergeBatchCommand(
    string Name,
    IReadOnlyList<string> PullRequestUrls);
