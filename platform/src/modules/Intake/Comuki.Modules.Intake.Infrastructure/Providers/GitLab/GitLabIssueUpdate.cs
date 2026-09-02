using System.Text.Json.Serialization;

namespace Comuki.Modules.Intake.Infrastructure.Providers.GitLab;

/// <summary>Issue state-event body.</summary>
public sealed record GitLabIssueUpdate([property: JsonPropertyName("state_event")] string StateEvent);
