using System.Text.Json.Serialization;

namespace Comuki.Modules.Intake.Infrastructure.Providers.Jira;

/// <summary>Transition request body.</summary>
public sealed record JiraTransitionBody(JiraTransitionRef Transition)
{
    /// <summary>Optional comment carried by the transition.</summary>
    [JsonPropertyName("update")]
    public JiraTransitionUpdate? Update { get; init; }
}
