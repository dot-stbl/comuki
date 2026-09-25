namespace Comuki.Shared.Editions.Licensing.Modes;

/// <summary>
/// How a verified <see cref="LicenseKey"/>'s feature/limit set is
/// matched against the catalogs at consumer sites. A smart type, not an
/// enum (anti-patterns.md §1.4 / smart-types.md §2): the closed set
/// belongs to the domain and is read once per token verify.
/// </summary>
public readonly record struct LicenseMode
{
    /// <summary>
    /// The default value (<c>default(LicenseMode)</c>). Never a legal
    /// mode for an actual license — observing it is a wire-shape
    /// violation, not a silent third option.
    /// </summary>
    public static LicenseMode Unspecified { get; }

    /// <summary>Every capability the licensed tier unlocks applies — the registry's rank-based comparison decides.</summary>
    public static LicenseMode ImplicitByRank { get; } = new("implicit-by-rank");

    /// <summary>Only the explicit <see cref="LicenseKey.Features"/> set applies; rank is informational.</summary>
    public static LicenseMode ExplicitAllowlist { get; } = new("explicit-allowlist");

    private readonly string? value;

    private LicenseMode(string value)
    {
        this.value = value;
    }

    /// <summary>Lowercase string form; <see cref="Unspecified"/> is <c>"unspecified"</c>.</summary>
    public string Value => value ?? "unspecified";

    /// <summary>Parses a payload's <c>mode</c> string into a <see cref="LicenseMode"/>.</summary>
    /// <param name="value">The wire string; compared ordinally against the two known values.</param>
    /// <param name="mode">The matched mode, or <c>default</c> when <paramref name="value"/> is unrecognised.</param>
    /// <returns><c>true</c> when <paramref name="value"/> matches a known mode.</returns>
    public static bool TryParse(string value, out LicenseMode mode)
    {
        if (string.Equals(value, ImplicitByRank.Value, StringComparison.Ordinal))
        {
            mode = ImplicitByRank;
            return true;
        }

        if (string.Equals(value, ExplicitAllowlist.Value, StringComparison.Ordinal))
        {
            mode = ExplicitAllowlist;
            return true;
        }

        mode = default;
        return false;
    }
}
