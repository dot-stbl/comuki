using Refit;

namespace Comuki.Modules.Intake.Infrastructure.Providers.GitHub;

/// <summary>
/// GitHub REST v3 surface used by intake: the issue catalog (inbox
/// mode) and the sync-back writes (comment + state). Paths are relative
/// — <see cref="TrackerClientFactory"/> binds the per-connection base
/// URL.
/// </summary>
public interface IGitHubApi
{
    /// <summary>Lists repository issues (includes PRs — the caller filters).</summary>
    /// <param name="owner"></param>
    /// <param name="repo"></param>
    /// <param name="state">open | closed | all.</param>
    /// <param name="perPage">Page size.</param>
    /// <param name="page">1-based page number.</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [Get("/repos/{owner}/{repo}/issues")]
    public Task<IReadOnlyList<GitHubIssueDto>> ListIssuesAsync(
        string owner,
        string repo,
        [AliasAs("state")] string state,
        [AliasAs("per_page")] int perPage,
        [AliasAs("page")] int page,
        CancellationToken cancellationToken);

    /// <summary>Posts a comment on an issue.</summary>
    /// <param name="owner"></param>
    /// <param name="repo"></param>
    /// <param name="issueNumber"></param>
    /// <param name="body"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [Post("/repos/{owner}/{repo}/issues/{issueNumber}/comments")]
    public Task PostCommentAsync(string owner, string repo, int issueNumber, [Body] GitHubCommentBody body, CancellationToken cancellationToken);

    /// <summary>Patches issue state (close on success).</summary>
    /// <param name="owner"></param>
    /// <param name="repo"></param>
    /// <param name="issueNumber"></param>
    /// <param name="body"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [Patch("/repos/{owner}/{repo}/issues/{issueNumber}")]
    public Task PatchIssueAsync(string owner, string repo, int issueNumber, [Body] GitHubIssueUpdate body, CancellationToken cancellationToken);
}
