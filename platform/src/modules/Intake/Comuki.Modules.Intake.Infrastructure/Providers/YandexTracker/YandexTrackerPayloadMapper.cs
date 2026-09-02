using System.Text.Json;
using Comuki.Modules.Intake.Domain.Tickets;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Intake.Infrastructure.Providers.YandexTracker;

/// <summary>
/// Yandex Tracker payload mapper — tolerant. The webhook wraps the
/// issue object (<c>{"issue": {"key": "COMUKI-5", …}}</c>); anything
/// without an issue object answers null.
/// </summary>
public static class YandexTrackerPayloadMapper
{
    /// <summary>Normalizes a webhook payload; null = not a ticket event.</summary>
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
            || !root.TryGetProperty("issue", out var issue)
            || issue.ValueKind is not JsonValueKind.Object)
        {
            return null;
        }

        var key = ReadString(issue, "key");
        return key.Length == 0
            ? null
            : IncomingTicket.Create(
            projectId,
            TicketProvider.YandexTracker,
            externalId: key,
            title: ReadString(issue, "summary"),
            body: ReadString(issue, "description"),
            author: ReadCreatedBy(issue),
            url: ReadString(issue, "self"),
            projectKey: ReadQueueKey(issue),
            labels: ReadTags(issue),
            now);
    }

    /// <summary>Maps a catalog issue DTO.</summary>
    /// <param name="issue"></param>
    /// <param name="projectId"></param>
    /// <param name="now"></param>
    /// <returns></returns>
    public static IncomingTicket ToTicket(TrackerIssueDto issue, ProjectId projectId, DateTimeOffset now)
    {
        return IncomingTicket.Create(
            projectId,
            TicketProvider.YandexTracker,
            externalId: issue.Key,
            title: issue.Summary,
            body: issue.Description ?? string.Empty,
            author: issue.CreatedByLogin,
            url: issue.Self,
            projectKey: issue.QueueKey,
            labels: [.. issue.Tags],
            now);
    }

    private static string ReadCreatedBy(JsonElement issue)
    {
        return issue.TryGetProperty("createdBy", out var user)
            && user.ValueKind is JsonValueKind.Object
            ? ReadString(user, "login")
            : string.Empty;
    }

    private static string ReadQueueKey(JsonElement issue)
    {
        return issue.TryGetProperty("queue", out var queue)
            && queue.ValueKind is JsonValueKind.Object
            ? ReadString(queue, "key")
            : string.Empty;
    }

    private static string[] ReadTags(JsonElement issue)
    {
        return !issue.TryGetProperty("tags", out var tags) || tags.ValueKind is not JsonValueKind.Array
            ? []
            : [.. tags.EnumerateArray()
            .Where(static tag => tag.ValueKind is JsonValueKind.String)
            .Select(static tag => tag.GetString() ?? string.Empty)
            .Where(static tag => tag.Length > 0)];
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
