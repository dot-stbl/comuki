namespace Comuki.Host.Intake.Models;

/// <summary>
/// Body for <c>POST /api/v1/sources/probe</c> — the operator types a
/// draft plus a plaintext credential in the connect form and asks the
/// host to reach the upstream before saving.
/// </summary>
public sealed class ProbeSourceDraftRequest
{
    /// <summary>Provider key (github | gitlab | yandex-tracker | jira).</summary>
    public required string Provider { get; init; } = string.Empty;

    /// <summary>Provider-specific, non-secret settings (apiBase, owner/repo, queue, …).</summary>
    public required string SettingsJson { get; init; } = "{}";

    /// <summary>Env-var name holding the outbound / webhook secret.</summary>
    public required string SecretEnvRef { get; init; } = string.Empty;
}
