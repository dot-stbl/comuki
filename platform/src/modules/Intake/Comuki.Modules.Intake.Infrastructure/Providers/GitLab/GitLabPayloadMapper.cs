using System.Text.Json;
using Comuki.Modules.Intake.Domain.Tickets;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Intake.Infrastructure.Providers.GitLab;

/// <summary>
/// GitLab payload mapper — tolerant: unknown fields ignored; anything
/// unparseable or not an issue event answers null.
/// </summary>
public static class GitLabPayloadMapper
{
    /// <summary>Ticket-relevant issue actions of the issue webhook.</summary>
    public static readonly IReadOnlySet<string> TicketActions =
        new HashSet<string>(["open", "reopen", "update"], StringComparer.Ordinal);

    /// <summary>Normalizes an issue webhook payload; null = not a ticket event.</summary>
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
        if (root.ValueKind is not JsonValueKind.Object
            || !root.TryGetProperty("object_kind", out var kind)
            || kind.GetString() != "issue"
            || !root.TryGetProperty("object_attributes", out var attributes)
            || attributes.ValueKind is not JsonValueKind.Object
            || !root.TryGetProperty("project", out var project)
            || project.ValueKind is not JsonValueKind.Object)
        {
            return null;
        }

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
            now);
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
