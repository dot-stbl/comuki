namespace Comuki.Host.Intake.Models;

/// <summary>Source connection creation body (POST /api/v1/sources).</summary>
public sealed class CreateSourceConnectionRequest
{
    /// <summary>Project the connection feeds.</summary>
    public required Guid ProjectId { get; init; }

    /// <summary>Provider key: github | gitlab | yandex-tracker | jira.</summary>
    public required string Provider { get; init; } = string.Empty;

    /// <summary>Human-readable name.</summary>
    public required string Name { get; init; } = string.Empty;

    /// <summary>Provider-specific, non-secret settings (env-var NAMES only).</summary>
    public required string SettingsJson { get; init; } = "{}";

    /// <summary>Env-var name holding the webhook secret.</summary>
    public required string SecretEnvRef { get; init; } = string.Empty;
}
