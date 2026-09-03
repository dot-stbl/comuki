using System.Text.Json;

namespace Comuki.Modules.Intake.Infrastructure.Providers.GitLab;

/// <summary>
/// GitLab connection settings (the connection's settings jsonb):
/// <c>{"projectId": 12345, "apiBase": "https://gitlab.com/api/v4",
/// "apiTokenEnv": "COMUKI_GL_TOKEN", "includeMergeRequests": true}</c>.
/// The numeric project id is the stable API handle; the base URL is
///overridable for self-hosted.
/// </summary>
/// <param name="ProjectId">Numeric GitLab project id.</param>
/// <param name="ProjectPath">Path with namespace (display only).</param>
/// <param name="ApiBase">API base URL.</param>
/// <param name="ApiTokenEnv">Env-var name holding the private token.</param>
/// <param name="IncludeMergeRequests">When true, the inbox catalog fetches MRs alongside issues.</param>
public sealed record GitLabSettings(
    int ProjectId,
    string ProjectPath,
    string ApiBase,
    string? ApiTokenEnv,
    bool IncludeMergeRequests)
{
    /// <summary>DefaultGitLab API base (overridable for self-hosted).</summary>
    public const string DefaultApiBase = "https://gitlab.com/api/v4";

    /// <summary>Tolerant parse of the settings jsonb.</summary>
    /// <param name="settingsJson"></param>
    /// <returns></returns>
    public static GitLabSettings Parse(string? settingsJson)
    {
        using var document = TrackerSettingsJson.Parse(settingsJson);
        var root = document?.RootElement;
        var projectId = root is { } element
            && element.TryGetProperty("projectId", out var id)
            && id.ValueKind is JsonValueKind.Number
            && id.TryGetInt32(out var value)
            ? value
            : 0;

        return new GitLabSettings(
            ProjectId: projectId,
            ProjectPath: ReadString(root, "projectPath"),
            ApiBase: ReadString(root, "apiBase") is { Length: > 0 } apiBase ? apiBase : DefaultApiBase,
            ApiTokenEnv: ReadString(root, "apiTokenEnv"),
            IncludeMergeRequests: ReadBool(root, "includeMergeRequests"));
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

    private static bool ReadBool(JsonElement? root, string property)
    {
        return root is { } element
            && element.TryGetProperty(property, out var value)
            && value.ValueKind is JsonValueKind.True;
    }
}
