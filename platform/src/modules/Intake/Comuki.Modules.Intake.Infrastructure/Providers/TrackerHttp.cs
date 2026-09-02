using System.Security.Cryptography;
using System.Text.Json;

namespace Comuki.Modules.Intake.Infrastructure.Providers;

/// <summary>
/// Named HTTP clients of the tracker providers — one registration per
/// tracker, each with the standard resilience handler (retry + circuit
/// breaker + total timeout); <see cref="TrackerClientFactory"/> shapes
/// the per-connection instance on top.
/// </summary>
public static class TrackerHttp
{
    /// <summary>Named client of the GitHub API.</summary>
    public const string GitHubClient = "intake-github";

    /// <summary>Named client of the GitLab API.</summary>
    public const string GitLabClient = "intake-gitlab";

    /// <summary>Named client of the Yandex Tracker API.</summary>
    public const string YandexTrackerClient = "intake-yandex-tracker";

    /// <summary>Named client of the Jira API.</summary>
    public const string JiraClient = "intake-jira";
}

/// <summary>
/// Delivery-id fallback: trackers without an id header (Yandex Tracker,
/// Jira) deduplicate on a SHA-256 of the raw body — the same payload
/// redelivered is the same letter; a different payload is a new one.
/// </summary>
public static class ProviderDeliveryIds
{
    /// <summary>The stable delivery id of a raw webhook body.</summary>
    /// <param name="body"></param>
    /// <returns></returns>
    public static string BodyHash(ReadOnlyMemory<byte> body)
    {
        var hash = SHA256.HashData(body.Span);
        return "sha256:" + Convert.ToHexStringLower(hash);
    }
}

/// <summary>Shared settings-json parse helper (tolerant, Web options).</summary>
internal static class TrackerSettingsJson
{
    public static JsonDocument? Parse(string? settingsJson)
    {
        if (string.IsNullOrWhiteSpace(settingsJson))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(settingsJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
