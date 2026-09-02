namespace Comuki.Modules.Intake.Infrastructure.Providers.Jira;

/// <summary>Jira issue fields wire shape.</summary>
public sealed record JiraFields
{
    /// <summary>Summary (title).</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>Description; may arrive null or non-string (ADF).</summary>
    public string? Description { get; init; }

    /// <summary>Project sub-object.</summary>
    public JiraProject? Project { get; init; }

    /// <summary>Project key.</summary>
    public string ProjectKey => Project?.Key ?? string.Empty;

    /// <summary>Creator sub-object.</summary>
    public JiraUser? Creator { get; init; }

    /// <summary>Creator display name.</summary>
    public string CreatorName => Creator?.DisplayName ?? string.Empty;

    /// <summary>Labels (plain strings on Jira).</summary>
    public IReadOnlyList<string> Labels { get; init; } = [];
}
