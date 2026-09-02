using Refit;

namespace Comuki.Modules.Intake.Infrastructure.Providers.GitLab;

/// <summary>
/// GitLab REST v4 surface used by intake: the issue catalog and the
/// sync-back writes (note + state event).
/// </summary>
public interface IGitLabApi
{
    /// <summary>Lists project issues.</summary>
    /// <param name="projectId">Numeric project id.</param>
    /// <param name="state">opened | closed | all.</param>
    /// <param name="perPage">Page size.</param>
    /// <param name="page">1-based page number.</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [Get("/projects/{projectId}/issues")]
    public Task<IReadOnlyList<GitLabIssue>> ListIssuesAsync(
        int projectId,
        [AliasAs("state")] string state,
        [AliasAs("per_page")] int perPage,
        [AliasAs("page")] int page,
        CancellationToken cancellationToken);

    /// <summary>Posts a note (comment) on an issue.</summary>
    /// <param name="projectId"></param>
    /// <param name="issueIid"></param>
    /// <param name="body"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [Post("/projects/{projectId}/issues/{issueIid}/notes")]
    public Task PostNoteAsync(int projectId, int issueIid, [Body] GitLabNoteBody body, CancellationToken cancellationToken);

    /// <summary>Patches the issue state (close on success).</summary>
    /// <param name="projectId"></param>
    /// <param name="issueIid"></param>
    /// <param name="body"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [Put("/projects/{projectId}/issues/{issueIid}")]
    public Task UpdateIssueAsync(int projectId, int issueIid, [Body] GitLabIssueUpdate body, CancellationToken cancellationToken);
}
