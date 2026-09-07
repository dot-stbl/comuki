using Comuki.Engine.Orchestration.Domain.MergeQueue;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Engine.Orchestration.Application.MergeQueue;

/// <summary>Application-layer command: enqueue one merge-queue entry.</summary>
/// <param name="ProjectId">Optional cross-project scope; null = release train.</param>
/// <param name="BranchName">Source branch name; non-empty.</param>
/// <param name="PullRequestUrl">Full PR URL the dashboard deep-links into; non-empty.</param>
/// <param name="ConflictResolution">Operator-declared strategy hint.</param>
/// <param name="Notes">Free-text notes; optional.</param>
public sealed record EnqueueMergeRequestCommand(
    ProjectId? ProjectId,
    string BranchName,
    string PullRequestUrl,
    ConflictResolution ConflictResolution,
    string? Notes);
