namespace Comuki.Modules.Intake.Infrastructure.Providers.Jira;

/// <summary>Jira project wire shape.</summary>
public sealed record JiraProject
{
    /// <summary>Project key.</summary>
    public string Key { get; init; } = string.Empty;
}
