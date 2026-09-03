using System.Text.Json;
using Comuki.Modules.Intake.Domain.Tickets;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Intake.Infrastructure.Providers.GitHub;

/// <summary>
/// GitHub payload mapper (external payload → domain ticket) — tolerant
/// by contract: unknown fields ignored; anything unparseable or not an
/// issue/PR event answers null (a logged skip, 200 OK upstream).
/// Admits two event surfaces:
/// <list type="bullet">
/// <item><c>issues</c> with actions <c>opened | reopened | labeled</c>.</item>
/// <item><c>pull_request</c> with actions <c>opened | ready_for_review | reopened</c> — the inbound PR-review surface (issue #27).</item>
/// </list>
/// </summary>
public static class GitHubPayloadMapper
{
    /// <summary>Ticket-relevant issue webhook actions.</summary>
    public static readonly IReadOnlySet<string> TicketActions =
        new HashSet<string>(["opened", "reopened", "labeled"], StringComparer.Ordinal);

    /// <summary>
    /// Ticket-relevant pull-request webhook actions (inbound review).
    /// <c>synchronize</c> / <c>closed</c> / <c>edited</c> / etc. are
    /// intentionally skipped for v1 — a push that needs another review
    /// is a follow-up feature (re-review policy).
    /// </summary>
    public static readonly IReadOnlySet<string> PullRequestActions =
        new HashSet<string>(["opened", "ready_for_review", "reopened"], StringComparer.Ordinal);

    /// <summary>Normalizes an issues / pull_request webhook payload; null = not a ticket event.</summary>
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
            || !root.TryGetProperty("action", out var actionElement)
            || actionElement.GetString() is not { } action
            || !root.TryGetProperty("repository", out var repository)
            || repository.ValueKind is not JsonValueKind.Object)
        {
            return null;
        }

        var fullName = ReadString(repository, "full_name");
        return fullName.Length == 0
            ? null
            : root.TryGetProperty("pull_request", out var pullRequest)
            && pullRequest.ValueKind is JsonValueKind.Object
            && PullRequestActions.Contains(action)
            ? MapPullRequest(pullRequest, fullName, projectId, now)
            : root.TryGetProperty("issue", out var issue)
            && issue.ValueKind is JsonValueKind.Object
            && TicketActions.Contains(action)
            ? MapIssue(issue, fullName, projectId, now)
            : null;
    }

    /// <summary>Maps a catalog issue DTO (the repository full name comes from settings).</summary>
    /// <param name="issue"></param>
    /// <param name="owner"></param>
    /// <param name="repo"></param>
    /// <param name="projectId"></param>
    /// <param name="now"></param>
    /// <returns></returns>
    public static IncomingTicket ToTicket(GitHubIssue issue, string owner, string repo, ProjectId projectId, DateTimeOffset now)
    {
        var projectKey = $"{owner}/{repo}";
        return IncomingTicket.Create(
            projectId,
            TicketProvider.GitHub,
            externalId: $"{projectKey}#{issue.Number}",
            title: issue.Title,
            body: issue.Body ?? string.Empty,
            author: issue.UserLogin,
            url: issue.HtmlUrl,
            projectKey: projectKey,
            labels: [.. issue.Labels.Select(static label => label.Name)],
            kind: issue.IsIssue ? InboundTicketKind.Issue : InboundTicketKind.PullRequest,
            now);
    }

    /// <summary>Parses "owner/repo#123" back into its parts; null when malformed.</summary>
    /// <param name="externalId"></param>
    /// <returns></returns>
    public static (string Owner, string Repo, int Number)? ParseExternalId(string externalId)
    {
        var hashIndex = externalId.IndexOf('#');
        return hashIndex <= 0
            || externalId[..hashIndex].Split('/') is not { Length: 2 } parts
            || !int.TryParse(externalId[(hashIndex + 1)..], out var number)
            ? null
            : (parts[0], parts[1], number);
    }

    private static IncomingTicket? MapIssue(JsonElement issue, string fullName, ProjectId projectId, DateTimeOffset now)
    {
        var number = ReadNumber(issue, "number");
        return number == 0
            ? null
            : IncomingTicket.Create(
            projectId,
            TicketProvider.GitHub,
            externalId: $"{fullName}#{number}",
            title: ReadString(issue, "title"),
            body: ReadString(issue, "body"),
            author: ReadAuthor(issue),
            url: ReadString(issue, "html_url"),
            projectKey: fullName,
            labels: ReadLabels(issue),
            kind: InboundTicketKind.Issue,
            now);
    }

    private static IncomingTicket? MapPullRequest(JsonElement pullRequest, string fullName, ProjectId projectId, DateTimeOffset now)
    {
        var number = ReadNumber(pullRequest, "number");
        return number == 0
            ? null
            : IncomingTicket.Create(
            projectId,
            TicketProvider.GitHub,
            externalId: $"{fullName}#{number}",
            title: ReadString(pullRequest, "title"),
            body: ReadString(pullRequest, "body"),
            author: ReadAuthor(pullRequest),
            url: ReadString(pullRequest, "html_url"),
            projectKey: fullName,
            labels: ReadLabels(pullRequest),
            kind: InboundTicketKind.PullRequest,
            now);
    }

    private static int ReadNumber(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var numberElement)
            && numberElement.ValueKind is JsonValueKind.Number
            && numberElement.TryGetInt32(out var value)
            ? value
            : 0;
    }

    private static string ReadAuthor(JsonElement issue)
    {
        return issue.TryGetProperty("user", out var user)
            && user.ValueKind is JsonValueKind.Object
            ? ReadString(user, "login")
            : string.Empty;
    }

    private static string[] ReadLabels(JsonElement issue)
    {
        return !issue.TryGetProperty("labels", out var labels) || labels.ValueKind is not JsonValueKind.Array
            ? []
            : [.. labels.EnumerateArray()
            .Where(static label => label.ValueKind is JsonValueKind.Object)
            .Select(static label => ReadString(label, "name"))
            .Where(static name => name.Length > 0)];
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
