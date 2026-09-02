using System.Text.Json;
using System.Text.Json.Serialization;

namespace Comuki.Modules.Intake.Infrastructure.Providers.GitHub;

/// <summary>
/// GitHub issue payload (wire shape) — tolerant: unknown fields are
/// ignored; <see cref="PullRequest"/> is non-null only on pull requests
/// (the catalog endpoint mixes them in).
/// </summary>
public sealed record GitHubIssue
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
    public GitHubUser? User { get; init; }

    /// <summary>Author login.</summary>
    public string UserLogin => User?.Login ?? string.Empty;

    /// <summary>Labels — the wire carries name/name pairs.</summary>
    public IReadOnlyList<GitHubLabel> Labels { get; init; } = [];

    /// <summary>Present only when the item is a pull request.</summary>
    [JsonPropertyName("pull_request")]
    public JsonElement? PullRequest { get; init; }

    /// <summary>True when the item is a real issue (not a PR).</summary>
    public bool IsIssue => PullRequest is null;
}
