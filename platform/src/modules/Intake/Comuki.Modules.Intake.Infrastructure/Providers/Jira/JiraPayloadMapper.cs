using System.Text.Json;
using Comuki.Modules.Intake.Domain.Tickets;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Intake.Infrastructure.Providers.Jira;

/// <summary>
/// Jira payload mapper — tolerant. Webhook events
/// (<c>jira:issue_created</c> / <c>jira:issue_updated</c>) wrap the
/// issue object; anything else answers null. The browsable URL is
/// composed from the connection's site (the payload's <c>self</c> link
/// is the API URL, not the browse URL).
/// </summary>
public static class JiraPayloadMapper
{
    /// <summary>Ticket-relevant webhook events.</summary>
    public static readonly IReadOnlySet<string> TicketEvents =
        new HashSet<string>(["jira:issue_created", "jira:issue_updated"], StringComparer.Ordinal);

    /// <summary>Normalizes a webhook payload; null = not a ticket event.</summary>
    /// <param name="body">Raw payload bytes.</param>
    /// <param name="site">Connection site base URL (browse links).</param>
    /// <param name="projectId">Project scope of the connection.</param>
    /// <param name="now">Ticket timestamp.</param>
    /// <returns></returns>
    public static IncomingTicket? ToTicket(ReadOnlyMemory<byte> body, string site, ProjectId projectId, DateTimeOffset now)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return ToTicket(document.RootElement, site, projectId, now);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Maps an already-parsed payload root; null = not a ticket event.</summary>
    /// <param name="root"></param>
    /// <param name="site"></param>
    /// <param name="projectId"></param>
    /// <param name="now"></param>
    /// <returns></returns>
    public static IncomingTicket? ToTicket(JsonElement root, string site, ProjectId projectId, DateTimeOffset now)
    {
        if (root.ValueKind is not JsonValueKind.Object
            || !root.TryGetProperty("webhookEvent", out var webhookEvent)
            || webhookEvent.GetString() is not { } eventName
            || !TicketEvents.Contains(eventName)
            || !root.TryGetProperty("issue", out var issue)
            || issue.ValueKind is not JsonValueKind.Object)
        {
            return null;
        }

        var key = ReadString(issue, "key");
        if (key.Length == 0)
        {
            return null;
        }

        var fields = issue.TryGetProperty("fields", out var fieldsElement) && fieldsElement.ValueKind is JsonValueKind.Object
            ? fieldsElement
            : (JsonElement?)null;

        return IncomingTicket.Create(
            projectId,
            TicketProvider.Jira,
            externalId: key,
            title: ReadString(fields, "summary"),
            body: ReadString(fields, "description"),
            author: ReadCreator(fields),
            url: site.Length > 0 ? $"{site.TrimEnd('/')}/browse/{key}" : string.Empty,
            projectKey: ReadProjectKey(fields),
            labels: ReadLabels(fields),
            now);
    }

    /// <summary>Maps a catalog issue DTO.</summary>
    /// <param name="issue"></param>
    /// <param name="site">Connection site base URL.</param>
    /// <param name="projectId"></param>
    /// <param name="now"></param>
    /// <returns></returns>
    public static IncomingTicket ToTicket(JiraIssueDto issue, string site, ProjectId projectId, DateTimeOffset now)
    {
        var fields = issue.Fields;
        return IncomingTicket.Create(
            projectId,
            TicketProvider.Jira,
            externalId: issue.Key,
            title: fields?.Summary ?? string.Empty,
            body: fields?.Description ?? string.Empty,
            author: fields?.CreatorName ?? string.Empty,
            url: site.Length > 0 ? $"{site.TrimEnd('/')}/browse/{issue.Key}" : string.Empty,
            projectKey: fields?.ProjectKey ?? string.Empty,
            labels: [.. fields?.Labels ?? []],
            now);
    }

    private static string ReadCreator(JsonElement? fields)
    {
        return fields is { } element
            && element.TryGetProperty("creator", out var creator)
            && creator.ValueKind is JsonValueKind.Object
            ? ReadString(creator, "displayName")
            : string.Empty;
    }

    private static string ReadProjectKey(JsonElement? fields)
    {
        return fields is { } element
            && element.TryGetProperty("project", out var project)
            && project.ValueKind is JsonValueKind.Object
            ? ReadString(project, "key")
            : string.Empty;
    }

    private static string[] ReadLabels(JsonElement? fields)
    {
        return fields is not { } element
            || !element.TryGetProperty("labels", out var labels)
            || labels.ValueKind is not JsonValueKind.Array
            ? []
            : [.. labels.EnumerateArray()
            .Where(static label => label.ValueKind is JsonValueKind.String)
            .Select(static label => label.GetString() ?? string.Empty)
            .Where(static label => label.Length > 0)];
    }

    private static string ReadString(JsonElement? element, string property)
    {
        return element is { } value
            && value.TryGetProperty(property, out var propertyValue)
            && propertyValue.ValueKind is JsonValueKind.String
            && propertyValue.GetString() is { } text
            ? text
            : string.Empty;
    }
}
