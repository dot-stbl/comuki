using Comuki.Engine.Orchestration.Domain.MergeQueue;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Engine.Orchestration.Application.MergeQueue;

/// <summary>Application-layer command: enqueue one merge-queue entry.</summary>
/// <param name="ProjectId">Optional cross-project scope; null = release train.</param>
/// <param name="BranchName">Source branch name; non-empty.</param>
/// <param name="PullRequestUrl">Full PR URL the dashboard deep-links into; non-empty.</param>
/// <param name="ConflictResolution">Operator-declared strategy hint.</param>
/// <param name="Notes">Free-text notes; optional.</param>
/// <param name="RunId">
/// Originating run (Coda — task 3.6 restore the run link). Null for
/// operator-driven entries that have no producing run. When supplied,
/// the platform stamps a <c>merge_queue.run_referenced</c> event in
/// the same transaction as the row insert. Optional and appended at
/// the end so pre-Coda callers compile unchanged.
/// </param>
public sealed record EnqueueMergeRequestCommand(
    ProjectId? ProjectId,
    string BranchName,
    string PullRequestUrl,
    ConflictResolution ConflictResolution,
    string? Notes,
    RunId? RunId = null);
