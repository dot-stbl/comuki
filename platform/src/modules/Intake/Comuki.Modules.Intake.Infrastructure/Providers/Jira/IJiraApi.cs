using Refit;

namespace Comuki.Modules.Intake.Infrastructure.Providers.Jira;

/// <summary>
/// Jira REST v2 surface used by intake: the JQL search (catalog),
/// comments and transitions.
/// </summary>
public interface IJiraApi
{
    /// <summary>Searches issues by JQL.</summary>
    /// <param name="jql">The JQL query.</param>
    /// <param name="maxResults">Page size.</param>
    /// <param name="startAt">Offset paging.</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [Get("/rest/api/2/search")]
    public Task<JiraSearchResult> SearchAsync(
        [AliasAs("jql")] string jql,
        [AliasAs("maxResults")] int maxResults,
        [AliasAs("startAt")] int startAt,
        CancellationToken cancellationToken);

    /// <summary>Posts a comment on an issue.</summary>
    /// <param name="issueKey">Issue key (e.g. COM-9).</param>
    /// <param name="body"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [Post("/rest/api/2/issue/{issueKey}/comment")]
    public Task PostCommentAsync(string issueKey, [Body] JiraCommentBody body, CancellationToken cancellationToken);

    /// <summary>Executes a status transition.</summary>
    /// <param name="issueKey">Issue key.</param>
    /// <param name="body">The transition (by id) with an optional comment.</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [Post("/rest/api/2/issue/{issueKey}/transitions")]
    public Task TransitionAsync(string issueKey, [Body] JiraTransitionBody body, CancellationToken cancellationToken);
}
