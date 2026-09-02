namespace Comuki.Modules.Intake.Infrastructure.Providers.Jira;

/// <summary>Jira search result envelope.</summary>
public sealed record JiraSearchResult
{
    /// <summary>Matching issues of the page.</summary>
    public IReadOnlyList<JiraIssue> Issues { get; init; } = [];

    /// <summary>Total matches.</summary>
    public int Total { get; init; }
}
