namespace Comuki.Modules.Integrations.Infrastructure.Providers.Jira;

/// <summary>Comment update payload of a transition.</summary>
public sealed record JiraTransitionUpdate
{
    /// <summary>Comment additions.</summary>
    public IReadOnlyList<JiraCommentAdd> Comment { get; init; } = [];
}
