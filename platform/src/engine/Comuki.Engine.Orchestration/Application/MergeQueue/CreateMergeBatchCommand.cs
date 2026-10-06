using Comuki.Shared.Kernel.Ids;

namespace Comuki.Engine.Orchestration.Application.MergeQueue;

/// <summary>Application-layer command: create a new merge batch.</summary>
/// <param name="Name">Operator-supplied human-readable batch name; non-empty.</param>
/// <param name="PullRequestUrls">Ordered PR URLs the batch ships; at least one, each non-empty.</param>
/// <param name="RunId">
/// Originating run (Coda — task 3.6 restore the run link). Null for
/// cross-project release trains or pre-orchestration operator seeds.
/// When supplied, the platform stamps a <c>merge_queue.run_referenced</c>
/// event in the same transaction as the row insert. Optional and
/// appended at the end so pre-Coda callers compile unchanged.
/// </param>
public sealed record CreateMergeBatchCommand(
    string Name,
    IReadOnlyList<string> PullRequestUrls,
    RunId? RunId = null);
