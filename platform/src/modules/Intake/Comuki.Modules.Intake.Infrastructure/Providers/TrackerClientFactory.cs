using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Refit;

namespace Comuki.Modules.Intake.Infrastructure.Providers;

/// <summary>
/// Builds per-connection Refit API proxies over the named tracker
/// clients (each registered with the standard resilience handler). A
/// fresh <see cref="HttpClient"/> from the factory per call keeps
/// handler pooling under the factory's control while the connection's
/// base URL and auth header land on the instance — self-hosted GitLab,
/// per-site Jira and per-org Yandex Tracker all work through the same
/// shape. The serializer uses the frozen Web defaults (the documented
/// Refit exception to the shared-options rule).
/// </summary>
/// <param name="httpClientFactory"></param>
public sealed class TrackerClientFactory(IHttpClientFactory httpClientFactory)
{
    private static readonly RefitSettings refitSettings = new()
    {
        ContentSerializer = new SystemTextJsonContentSerializer(JsonSerializerOptions.Web),
    };

    /// <summary>A GitHub API client bound to the connection's settings.</summary>
    /// <param name="apiBase">API base URL (public or enterprise).</param>
    /// <param name="token">Bearer token (PAT); null for anonymous.</param>
    /// <returns></returns>
    public GitHub.IGitHubApi GitHub(string apiBase, string? token)
    {
        var http = httpClientFactory.CreateClient(TrackerHttp.GitHubClient);
        http.BaseAddress = new Uri(apiBase);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        TrackerHttpHeaders.ApplyBearer(http, token);
        return RestService.For<GitHub.IGitHubApi>(http, refitSettings);
    }

    /// <summary>A GitLab API client bound to the connection's settings.</summary>
    /// <param name="apiBase">API base URL (gitlab.com or self-hosted, with /api/v4).</param>
    /// <param name="token">Private token.</param>
    /// <returns></returns>
    public GitLab.IGitLabApi GitLab(string apiBase, string? token)
    {
        var http = httpClientFactory.CreateClient(TrackerHttp.GitLabClient);
        http.BaseAddress = new Uri(apiBase);
        if (token is { Length: > 0 })
        {
            http.DefaultRequestHeaders.Add("PRIVATE-TOKEN", token);
        }

        return RestService.For<GitLab.IGitLabApi>(http, refitSettings);
    }

    /// <summary>A Yandex Tracker API client bound to the connection's settings.</summary>
    /// <param name="apiBase">API base URL (default https://api.tracker.yandex.net).</param>
    /// <param name="token">OAuth token.</param>
    /// <param name="orgId">Organization id (X-Org-Id header).</param>
    /// <returns></returns>
    public YandexTracker.IYandexTrackerApi YandexTracker(string apiBase, string? token, string? orgId)
    {
        var http = httpClientFactory.CreateClient(TrackerHttp.YandexTrackerClient);
        http.BaseAddress = new Uri(apiBase);
        if (token is { Length: > 0 })
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("OAuth", token);
        }

        if (orgId is { Length: > 0 })
        {
            http.DefaultRequestHeaders.Add("X-Org-Id", orgId);
        }

        return RestService.For<YandexTracker.IYandexTrackerApi>(http, refitSettings);
    }

    /// <summary>A Jira API client bound to the connection's settings.</summary>
    /// <param name="site">Site base URL (https://{site}.atlassian.net).</param>
    /// <param name="basicCredential">"email:api-token" pair for basic auth.</param>
    /// <returns></returns>
    public Jira.IJiraApi Jira(string site, string? basicCredential)
    {
        var http = httpClientFactory.CreateClient(TrackerHttp.JiraClient);
        http.BaseAddress = new Uri(site);
        if (basicCredential is { Length: > 0 })
        {
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(basicCredential));
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", encoded);
        }

        return RestService.For<Jira.IJiraApi>(http, refitSettings);
    }
}

/// <summary>Auth-header helpers shared by the factory methods.</summary>
file static class TrackerHttpHeaders
{
    public static void ApplyBearer(HttpClient http, string? token)
    {
        if (token is { Length: > 0 })
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }
}
