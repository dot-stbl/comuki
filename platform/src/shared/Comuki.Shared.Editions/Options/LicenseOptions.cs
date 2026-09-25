namespace Comuki.Shared.Editions.Options;

/// <summary>
/// Operator-facing license configuration (issue #164). Bound from
/// <c>Host:License</c>. Absent config -&gt; Community (no error);
/// present-but-unresolvable <see cref="Path"/> fails boot loudly via
/// <see cref="LicenseOptionsValidator"/>; present-but-cryptographically-
/// invalid license degrades to Community at runtime with a logged
/// warning (see <see cref="Edition.LicenseEdition"/>) — neither
/// present-but-bad path crashes the host.
/// </summary>
public sealed class LicenseOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Host:License";

    /// <summary>
    /// Operator-facing secret reference (e.g. <c>file:/etc/comuki/license.key</c>,
    /// <c>env:COMUKI_LICENSE</c>), same shape
    /// <see cref="Kernel.Secrets.ISecretResolver.ResolveAsync"/>
    /// accepts everywhere else. Null/absent means no license is configured
    /// — Community, not an error.
    /// </summary>
    public string? Path { get; init; }

    /// <summary>
    /// How long after <see cref="Licensing.LicenseKey.Expiry"/> a paid
    /// license keeps working with a visible warning before the gate
    /// degrades. Must not be negative — the
    /// <see cref="Licensing.Status.LicenseEvaluator"/> range-checks this
    /// at classify time and the boot validator checks it here, so a
    /// negative value is a hard fail.
    /// </summary>
    public TimeSpan GracePeriod { get; init; } = TimeSpan.FromDays(14);

    /// <summary>
    /// Minimum interval between re-checks of the underlying secret/file.
    /// Must be strictly positive — a non-positive value would busy-loop
    /// re-verifying on every single <see cref="Edition.IEdition.Has"/> /
    /// <see cref="Edition.IEdition.Limit"/> call. The boot validator
    /// range-checks this here.
    /// </summary>
    public TimeSpan ReloadDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Base64 of a 32-byte Ed25519 verifying key trusted <em>only</em>
    /// for dev-audience licenses. A dev-license token signed by a key
    /// other than this one is rejected at verify time; a non-dev token
    /// (production audience) is verified against the embedded
    /// production key regardless of this value. Leave unset on
    /// production contours — that's the rule that keeps a leaked dev
    /// license from unlocking production. Env override:
    /// <c>COMUKI_HOST_LICENSE_DEVPUBLICKEY</c>.
    /// </summary>
    public string? DevPublicKey { get; init; }
}
