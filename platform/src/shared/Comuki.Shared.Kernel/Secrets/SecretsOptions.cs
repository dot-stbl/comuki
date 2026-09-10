namespace Comuki.Shared.Kernel.Secrets;

/// <summary>
/// Root options for the secret-resolution subsystem (issue #52). The
/// section is opt-in: per-provider sub-sections default to
/// <c>Enabled = false</c>, so a deployment that ships only env-var
/// references pays zero bootstrap cost and registers no remote
/// providers. Each per-provider sub-section exposes its own
/// <see cref="FileSecretOptions"/> shape (slice 1); the slice-2/3 Vault
/// and Consul providers extend this with their own typed options.
/// Bound from the <c>[Secrets]</c> configuration section with
/// <c>ValidateOnStart</c> so a misconfigured provider fails the boot, not
/// the first request.
/// </summary>
public sealed class SecretsOptions
{
    /// <summary>Configuration section name (<c>[Secrets]</c> in TOML / <c>Secrets:*</c> in appsettings).</summary>
    public const string SectionName = "Secrets";

    /// <summary>Per-provider sub-sections. Keyed by the provider's <see cref="ISecretProvider.Scheme"/>.</summary>
    public IReadOnlyDictionary<string, FileSecretOptions> Providers { get; init; } =
        new Dictionary<string, FileSecretOptions>(StringComparer.OrdinalIgnoreCase);
}
