using System.Text.Json;
using System.Text.Json.Serialization;

namespace Comuki.Modules.Intake.Infrastructure.Providers.GitHub;

/// <summary>
/// GitHub issue payload (wire DTO) — tolerant: unknown fields are
/// ignored; <see cref="PullRequest"/> is non-null only on pull requests
/// (the catalog endpoint mixes them in).
/// </summary>
public sealed record GitHubIssueDto
{
    /// <summary>Issue number.</summary>
    public int Number { get; init; }

    /// <summary>Issue title.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Issue body (markdown); may arrive null.</summary>
    public string? Body { get; init; }

    /// <summary>Browsable issue URL.</summary>
    public string HtmlUrl { get; init; } = string.Empty;

    /// <summary>Author sub-object.</summary>
    [JsonPropertyName("user")]
    public GitHubUserDto? User { get; init; }

    /// <summary>Author login.</summary>
    public string UserLogin => User?.Login ?? string.Empty;

    /// <summary>Labels — the wire carries name/name pairs.</summary>
    public IReadOnlyList<GitHubLabelDto> Labels { get; init; } = [];

    /// <summary>Present only when the item is a pull request.</summary>
    [JsonPropertyName("pull_request")]
    public JsonElement? PullRequest { get; init; }

    /// <summary>True when the item is a real issue (not a PR).</summary>
    public bool IsIssue => PullRequest is null;
}

/// <summary>GitHub label wire shape.</summary>
public sealed record GitHubLabelDto
{
    /// <summary>Label name.</summary>
    public string Name { get; init; } = string.Empty;
}

/// <summary>GitHub user wire shape.</summary>
public sealed record GitHubUserDto
{
    /// <summary>User login.</summary>
    public string Login { get; init; } = string.Empty;
}

/// <summary>Comment request body.</summary>
public sealed record GitHubCommentBody(string Body);

/// <summary>Issue state patch body.</summary>
public sealed record GitHubIssueUpdate(string State);
