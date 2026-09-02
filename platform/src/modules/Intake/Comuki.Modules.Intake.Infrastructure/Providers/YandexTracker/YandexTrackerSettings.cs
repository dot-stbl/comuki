using System.Text.Json;

namespace Comuki.Modules.Intake.Infrastructure.Providers.YandexTracker;

/// <summary>
/// Yandex Tracker connection settings (the connection's settings
/// jsonb): <c>{"queue": "COMUKI", "orgId": "12345", "apiBase": "…",
/// "apiTokenEnv": "COMUKI_YT_TOKEN", "resolvedTransition": "done",
/// "webhookSecretHeader": "X-Tracker-Token"}</c>.
/// </summary>
/// <param name="Queue">Queue key — the catalog query and the project filter.</param>
/// <param name="OrgId">Organization id (the X-Org-Id header of the API).</param>
/// <param name="ApiBase">API base URL.</param>
/// <param name="ApiTokenEnv">Env-var name holding the OAuth token.</param>
/// <param name="ResolvedTransition">Transition key applied on run success (e.g. "done").</param>
/// <param name="WebhookSecretHeader">Header compared against the webhook secret (default X-Tracker-Token).</param>
public sealed record YandexTrackerSettings(
    string Queue,
    string OrgId,
    string ApiBase,
    string? ApiTokenEnv,
    string ResolvedTransition,
    string WebhookSecretHeader)
{
    /// <summary>Default Yandex Tracker API base.</summary>
    public const string DefaultApiBase = "https://api.tracker.yandex.net";

    /// <summary>Default webhook secret header name.</summary>
    public const string DefaultWebhookSecretHeader = "X-Tracker-Token";

    /// <summary>Tolerant parse of the settings jsonb.</summary>
    /// <param name="settingsJson"></param>
    /// <returns></returns>
    public static YandexTrackerSettings Parse(string? settingsJson)
    {
        using var document = TrackerSettingsJson.Parse(settingsJson);
        var root = document?.RootElement;

        return new YandexTrackerSettings(
            Queue: ReadString(root, "queue"),
            OrgId: ReadString(root, "orgId"),
            ApiBase: ReadString(root, "apiBase") is { Length: > 0 } apiBase ? apiBase : DefaultApiBase,
            ApiTokenEnv: ReadString(root, "apiTokenEnv"),
            ResolvedTransition: ReadString(root, "resolvedTransition") is { Length: > 0 } transition ? transition : "done",
            WebhookSecretHeader: ReadString(root, "webhookSecretHeader") is { Length: > 0 } header ? header : DefaultWebhookSecretHeader);
    }

    private static string ReadString(JsonElement? root, string property)
    {
        return root is { } element
            && element.TryGetProperty(property, out var value)
            && value.ValueKind is JsonValueKind.String
            && value.GetString() is { Length: > 0 } text
            ? text
            : string.Empty;
    }
}
