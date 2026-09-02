using System.Text.Json;

namespace Comuki.Modules.Intake.Infrastructure.Providers.Jira;

/// <summary>
/// Jira connection settings (the connection's settings jsonb):
/// <c>{"site": "https://acme.atlassian.net", "project": "COM",
/// "jql": "project = COM ORDER BY created DESC", "apiTokenEnv":
/// "COMUKI_JIRA_CRED", "resolvedTransitionId": "31",
/// "webhookSecretParam": "secret"}</c>. The api token env holds the
/// basic-auth pair <c>email:api-token</c>.
/// </summary>
/// <param name="Site">Site base URL.</param>
/// <param name="Project">Project key — the default JQL scope and the ticket project filter.</param>
/// <param name="Jql">Explicit JQL override for the catalog.</param>
/// <param name="ApiTokenEnv">Env-var name holding "email:api-token".</param>
/// <param name="ResolvedTransitionId">Transition id applied on run success.</param>
/// <param name="WebhookSecretParam">Query param compared against the webhook secret (default "secret").</param>
public sealed record JiraSettings(
    string Site,
    string Project,
    string Jql,
    string? ApiTokenEnv,
    string ResolvedTransitionId,
    string WebhookSecretParam)
{
    /// <summary>Default webhook secret query parameter name.</summary>
    public const string DefaultWebhookSecretParam = "secret";

    /// <summary>Tolerant parse of the settings jsonb.</summary>
    /// <param name="settingsJson"></param>
    /// <returns></returns>
    public static JiraSettings Parse(string? settingsJson)
    {
        using var document = TrackerSettingsJson.Parse(settingsJson);
        var root = document?.RootElement;
        var project = ReadString(root, "project");
        var explicitJql = ReadString(root, "jql");

        return new JiraSettings(
            Site: ReadString(root, "site"),
            Project: project,
            Jql: explicitJql.Length > 0
                ? explicitJql
                : project.Length > 0 ? $"project = {project} AND resolution = Unresolved ORDER BY created DESC" : string.Empty,
            ApiTokenEnv: ReadString(root, "apiTokenEnv"),
            ResolvedTransitionId: ReadString(root, "resolvedTransitionId"),
            WebhookSecretParam: ReadString(root, "webhookSecretParam") is { Length: > 0 } param ? param : DefaultWebhookSecretParam);
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
