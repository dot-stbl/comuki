using System.Text.Json.Serialization;

namespace Comuki.Modules.Intake.Infrastructure.Providers.GitLab;

/// <summary>GitLab issue wire DTO — tolerant: unknown fields ignored.</summary>
public sealed record GitLabIssueDto
{
    /// <summary>Issue iid (project-scoped number).</summary>
    public int Iid { get; init; }

    /// <summary>Issue title.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Issue description (markdown); may arrive null.</summary>
    public string? Description { get; init; }

    /// <summary>Browsable issue URL.</summary>
    public string WebUrl { get; init; } = string.Empty;

    /// <summary>Author sub-object.</summary>
    public GitLabUserDto? Author { get; init; }

    /// <summary>Author username.</summary>
    public string AuthorName => Author?.Username ?? string.Empty;

    /// <summary>Labels (plain strings on GitLab).</summary>
    public IReadOnlyList<string> Labels { get; init; } = [];
}

/// <summary>GitLab user wire shape.</summary>
public sealed record GitLabUserDto
{
    /// <summary>Username.</summary>
    public string Username { get; init; } = string.Empty;
}

/// <summary>Note request body.</summary>
public sealed record GitLabNoteBody(string Body);

/// <summary>Issue state-event body.</summary>
public sealed record GitLabIssueUpdate([property: JsonPropertyName("state_event")] string StateEvent);
