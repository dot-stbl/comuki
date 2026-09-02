using System.Text.Json;

namespace Comuki.Modules.Intake.Infrastructure.Providers.GitHub;

/// <summary>
/// GitHub connection settings (the connection's settings jsonb):
/// <c>{"owner": "dot-stbl", "repo": "comuki", "apiBase": "…",
/// "apiTokenEnv": "COMUKI_GH_TOKEN"}</c>. Parsing is tolerant — missing
/// fields fall back to the defaults, a broken json reads as empty.
/// </summary>
/// <param name="Owner">Repository owner.</param>
/// <param name="Repo">Repository name.</param>
/// <param name="ApiBase">API base URL.</param>
/// <param name="ApiTokenEnv">Env-var name holding the PAT for outbound calls.</param>
public sealed record GitHubSettings(
    string Owner,
    string Repo,
    string ApiBase,
    string? ApiTokenEnv)
{
    /// <summary>Default GitHub API base (overridable for enterprise).</summary>
    public const string DefaultApiBase = "https://api.github.com";

    /// <summary>Tolerant parse of the settings jsonb.</summary>
    /// <param name="settingsJson"></param>
    /// <returns></returns>
    public static GitHubSettings Parse(string? settingsJson)
    {
        using var document = TrackerSettingsJson.Parse(settingsJson);
        var root = document?.RootElement;

        return new GitHubSettings(
            Owner: ReadString(root, "owner"),
            Repo: ReadString(root, "repo"),
            ApiBase: ReadString(root, "apiBase") is { Length: > 0 } apiBase ? apiBase : DefaultApiBase,
            ApiTokenEnv: ReadString(root, "apiTokenEnv"));
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
