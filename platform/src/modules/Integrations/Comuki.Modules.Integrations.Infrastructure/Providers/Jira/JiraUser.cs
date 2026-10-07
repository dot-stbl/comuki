namespace Comuki.Modules.Integrations.Infrastructure.Providers.Jira;

/// <summary>Jira user wire shape.</summary>
public sealed record JiraUser
{
    /// <summary>Display name.</summary>
    public string DisplayName { get; init; } = string.Empty;
}
