using System.Text.Json.Serialization;

namespace Comuki.Modules.Intake.Infrastructure.Providers.Jira;

/// <summary>Jira search result envelope.</summary>
public sealed record JiraSearchResult
{
    /// <summary>Matching issues of the page.</summary>
    public IReadOnlyList<JiraIssueDto> Issues { get; init; } = [];

    /// <summary>Total matches.</summary>
    public int Total { get; init; }
}

/// <summary>Jira issue wire DTO — tolerant: unknown fields ignored.</summary>
public sealed record JiraIssueDto
{
    /// <summary>Issue key (e.g. COM-9).</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Issue fields sub-object.</summary>
    public JiraIssueFieldsDto? Fields { get; init; }
}

/// <summary>Jira issue fields wire shape.</summary>
public sealed record JiraIssueFieldsDto
{
    /// <summary>Summary (title).</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>Description; may arrive null or non-string (ADF).</summary>
    public string? Description { get; init; }

    /// <summary>Project sub-object.</summary>
    public JiraProjectDto? Project { get; init; }

    /// <summary>Project key.</summary>
    public string ProjectKey => Project?.Key ?? string.Empty;

    /// <summary>Creator sub-object.</summary>
    public JiraUserDto? Creator { get; init; }

    /// <summary>Creator display name.</summary>
    public string CreatorName => Creator?.DisplayName ?? string.Empty;

    /// <summary>Labels (plain strings on Jira).</summary>
    public IReadOnlyList<string> Labels { get; init; } = [];
}

/// <summary>Jira project wire shape.</summary>
public sealed record JiraProjectDto
{
    /// <summary>Project key.</summary>
    public string Key { get; init; } = string.Empty;
}

/// <summary>Jira user wire shape.</summary>
public sealed record JiraUserDto
{
    /// <summary>Display name.</summary>
    public string DisplayName { get; init; } = string.Empty;
}

/// <summary>Comment request body.</summary>
public sealed record JiraCommentBody(string Body);

/// <summary>Transition request body.</summary>
public sealed record JiraTransitionBody(JiraTransitionRef Transition)
{
    /// <summary>Optional comment carried by the transition.</summary>
    [JsonPropertyName("update")]
    public JiraTransitionUpdate? Update { get; init; }
}

/// <summary>Transition reference by id.</summary>
public sealed record JiraTransitionRef(string Id);

/// <summary>Comment update payload of a transition.</summary>
public sealed record JiraTransitionUpdate
{
    /// <summary>Comment additions.</summary>
    public IReadOnlyList<JiraCommentAdd> Comment { get; init; } = [];
}

/// <summary>One comment addition.</summary>
public sealed record JiraCommentAdd(JiraCommentBody Add);
