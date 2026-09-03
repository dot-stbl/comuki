namespace Comuki.Modules.Intake.Infrastructure.Providers.GitLab;

/// <summary>GitLab merge-request wire shape — tolerant: unknown fields ignored.</summary>
public sealed record GitLabMergeRequest
{
    /// <summary>MR iid (project-scoped number).</summary>
    public int Iid { get; init; }

    /// <summary>MR title.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>MR description (markdown); may arrive null.</summary>
    public string? Description { get; init; }

    /// <summary>Browsable MR URL.</summary>
    public string WebUrl { get; init; } = string.Empty;

    /// <summary>Author sub-object.</summary>
    public GitLabUser? Author { get; init; }

    /// <summary>Author username.</summary>
    public string AuthorName => Author?.Username ?? string.Empty;

    /// <summary>Labels (plain strings on GitLab).</summary>
    public IReadOnlyList<string> Labels { get; init; } = [];
}
