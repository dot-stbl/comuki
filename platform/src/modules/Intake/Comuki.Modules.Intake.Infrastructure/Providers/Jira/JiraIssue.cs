namespace Comuki.Modules.Intake.Infrastructure.Providers.Jira;

/// <summary>Jira issue wire shape — tolerant: unknown fields ignored.</summary>
public sealed record JiraIssue
{
    /// <summary>Issue key (e.g. COM-9).</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Issue fields sub-object.</summary>
    public JiraFields? Fields { get; init; }
}
