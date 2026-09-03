using System.Text.Json;
using Comuki.Modules.Intake.Domain.Tickets;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Intake.Infrastructure.Providers.GitLab;

/// <summary>
/// GitLab payload mapper — tolerant: unknown fields ignored; anything
/// unparseable or not an issue / merge-request event answers null.
/// Admits two object kinds:
/// <list type="bullet">
/// <item><c>object_kind == "issue"</c> with the issue action set.</item>
/// <item><c>object_kind == "merge_request"</c> with the MR action set (inbound PR-review surface, issue #27).</item>
/// </list>
/// </summary>
public static class GitLabPayloadMapper
{
    /// <summary>Ticket-relevant issue actions of the issue webhook.</summary>
    public static readonly IReadOnlySet<string> TicketActions =
        new HashSet<string>(["open", "reopen", "update"], StringComparer.Ordinal);

    /// <summary>
    /// Ticket-relevant merge-request actions (inbound review).
    /// <c>merge</c> / <c>close</c> / <c>synchronize</c> are intentionally
    /// skipped for v1 — a push that needs another review is a follow-up.
    /// </summary>
    public static readonly IReadOnlySet<string> MergeRequestActions =
        new HashSet<string>(["open", "reopen", "update"], StringComparer.Ordinal);

    /// <summary>Normalizes an issue / merge-request webhook payload; null = not a ticket event.</summary>
    /// <param name="body">Raw payload bytes.</param>
    /// <param name="projectId">Project scope of the connection.</param>
    /// <param name="now">Ticket timestamp.</param>
    /// <returns></returns>
    public static IncomingTicket? ToTicket(ReadOnlyMemory<byte> body, ProjectId projectId, DateTimeOffset now)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return ToTicket(document.RootElement, projectId, now);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Maps an already-parsed payload root; null = not a ticket event.</summary>
    /// <param name="root"></param>
    /// <param name="projectId"></param>
    /// <param name="now"></param>
    /// <returns></returns>
    public static IncomingTicket? ToTicket(JsonElement root, ProjectId projectId, DateTimeOffset now)
    {
        return root.ValueKind is JsonValueKind.Object
            && root.TryGetProperty("object_kind", out var kind)
            && kind.GetString() is { } objectKind
            && root.TryGetProperty("object_attributes", out var attributes)
            && attributes.ValueKind is JsonValueKind.Object
            && root.TryGetProperty("project", out var project)
            && project.ValueKind is JsonValueKind.Object
            ? objectKind switch
            {
                "issue" => MapIssue(root, attributes, project, projectId, now),
                "merge_request" => MapMergeRequest(root, attributes, project, projectId, now),
                _ => null,
            }
            : null;
    }

    /// <summary>Maps a catalog issue DTO.</summary>
    /// <param name="issue"></param>
    /// <param name="projectPath">Path with namespace (settings).</param>
    /// <param name="projectId"></param>
    /// <param name="now"></param>
    /// <returns></returns>
    public static IncomingTicket ToTicket(GitLabIssue issue, string projectPath, ProjectId projectId, DateTimeOffset now)
    {
        return IncomingTicket.Create(
            projectId,
            TicketProvider.GitLab,
            externalId: $"{projectPath}#{issue.Iid}",
            title: issue.Title,
            body: issue.Description ?? string.Empty,
            author: issue.AuthorName,
            url: issue.WebUrl,
            projectKey: projectPath,
            labels: [.. issue.Labels],
            kind: InboundTicketKind.Issue,
            now);
    }

    /// <summary>Maps a catalog merge-request DTO (inbound review).</summary>
    /// <param name="mergeRequest"></param>
    /// <param name="projectPath">Path with namespace (settings).</param>
    /// <param name="projectId"></param>
    /// <param name="now"></param>
    /// <returns></returns>
    public static IncomingTicket ToTicket(GitLabMergeRequest mergeRequest, string projectPath, ProjectId projectId, DateTimeOffset now)
    {
        return IncomingTicket.Create(
            projectId,
            TicketProvider.GitLab,
            externalId: $"{projectPath}#{mergeRequest.Iid}",
            title: mergeRequest.Title,
            body: mergeRequest.Description ?? string.Empty,
            author: mergeRequest.AuthorName,
            url: mergeRequest.WebUrl,
            projectKey: projectPath,
            labels: [.. mergeRequest.Labels],
            kind: InboundTicketKind.PullRequest,
            now);
    }

    private static IncomingTicket? MapIssue(JsonElement root, JsonElement attributes, JsonElement project, ProjectId projectId, DateTimeOffset now)
    {
        // update events carry no action for label-only changes via API —
        // absent action counts as relevant
        if (attributes.TryGetProperty("action", out var actionElement)
            && actionElement.GetString() is { } action
            && !TicketActions.Contains(action))
        {
            return null;
        }

        var projectPath = ReadString(project, "path_with_namespace");
        var iid = attributes.TryGetProperty("iid", out var iidElement) && iidElement.ValueKind is JsonValueKind.Number
            ? iidElement.GetInt32()
            : 0;

        return projectPath.Length == 0 || iid == 0
            ? null
            : IncomingTicket.Create(
            projectId,
            TicketProvider.GitLab,
            externalId: $"{projectPath}#{iid}",
            title: ReadString(attributes, "title"),
            body: ReadString(attributes, "description"),
            author: ReadAuthor(root),
            url: ReadString(attributes, "url"),
            projectKey: projectPath,
            labels: ReadLabels(root),
            kind: InboundTicketKind.Issue,
            now);
    }

    private static IncomingTicket? MapMergeRequest(JsonElement root, JsonElement attributes, JsonElement project, ProjectId projectId, DateTimeOffset now)
    {
        if (attributes.TryGetProperty("action", out var actionElement)
            && actionElement.GetString() is { } action
            && !MergeRequestActions.Contains(action))
        {
            return null;
        }

        var projectPath = ReadString(project, "path_with_namespace");
        var iid = attributes.TryGetProperty("iid", out var iidElement) && iidElement.ValueKind is JsonValueKind.Number
            ? iidElement.GetInt32()
            : 0;

        return projectPath.Length == 0 || iid == 0
            ? null
            : IncomingTicket.Create(
            projectId,
            TicketProvider.GitLab,
            externalId: $"{projectPath}#{iid}",
            title: ReadString(attributes, "title"),
            body: ReadString(attributes, "description"),
            author: ReadAuthor(root),
            url: ReadString(attributes, "url"),
            projectKey: projectPath,
            labels: ReadLabels(root),
            kind: InboundTicketKind.PullRequest,
            now);
    }

    private static string ReadAuthor(JsonElement root)
    {
        return root.TryGetProperty("user", out var user)
            && user.ValueKind is JsonValueKind.Object
            ? ReadString(user, "username")
            : string.Empty;
    }

    private static string[] ReadLabels(JsonElement root)
    {
        return !root.TryGetProperty("labels", out var labels) || labels.ValueKind is not JsonValueKind.Array
            ? []
            : [.. labels.EnumerateArray()
            .Where(static label => label.ValueKind is JsonValueKind.Object)
            .Select(static label => ReadString(label, "title"))
            .Where(static title => title.Length > 0)];
    }

    private static string ReadString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value)
            && value.ValueKind is JsonValueKind.String
            && value.GetString() is { } text
            ? text
            : string.Empty;
    }
}
