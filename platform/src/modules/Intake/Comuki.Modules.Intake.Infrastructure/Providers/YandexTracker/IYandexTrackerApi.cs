using Refit;

namespace Comuki.Modules.Intake.Infrastructure.Providers.YandexTracker;

/// <summary>
/// Yandex Tracker API v2 surface used by intake: the issue search
/// (catalog), comments and status transitions.
/// </summary>
public interface IYandexTrackerApi
{
    /// <summary>Searches issues by query (the catalog).</summary>
    /// <param name="body">The search request (HQL query text).</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [Post("/v2/issues/_search")]
    public Task<IReadOnlyList<TrackerIssue>> SearchIssuesAsync([Body] TrackerSearchBody body, CancellationToken cancellationToken);

    /// <summary>Posts a comment on an issue.</summary>
    /// <param name="issueKey">Issue key (e.g. COMUKI-5).</param>
    /// <param name="body"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [Post("/v2/issues/{issueKey}/comments")]
    public Task PostCommentAsync(string issueKey, [Body] TrackerCommentBody body, CancellationToken cancellationToken);

    /// <summary>Executes a status transition (e.g. resolve on success).</summary>
    /// <param name="issueKey">Issue key.</param>
    /// <param name="transition">Transition key or id.</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [Post("/v2/issues/{issueKey}/transitions/{transition}")]
    public Task TransitionAsync(string issueKey, string transition, CancellationToken cancellationToken);
}
