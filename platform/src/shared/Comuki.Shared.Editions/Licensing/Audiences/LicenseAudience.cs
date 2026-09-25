namespace Comuki.Shared.Editions.Licensing.Audiences;

/// <summary>
/// Which contour a license was minted for. The verifier selects its
/// Ed25519 verifying key from the payload's <c>audience</c> field —
/// <see cref="Production"/> tokens verify only against the production
/// key, <see cref="Dev"/> tokens only against the dev-overlay key. A
/// leaked dev token cannot unlock a production contour because the
/// production contour never configures the dev verifying key. A smart
/// type, not an enum (anti-patterns.md §1.4 / smart-types.md §2): the
/// closed set belongs to the domain and is read once per token verify.
/// </summary>
public readonly record struct LicenseAudience
{
    /// <summary>
    /// The default value (<c>default(LicenseAudience)</c>). Never a
    /// legal audience for an actual license — the verifier maps an
    /// absent payload <c>audience</c> field to <see cref="Production"/>
    /// for backwards compatibility, but observing <see cref="Unspecified"/>
    /// anywhere downstream of that mapping is a wire-shape violation.
    /// </summary>
    public static LicenseAudience Unspecified { get; }

    /// <summary>The default contour — a license minted by the business signing key, intended for customer deployments.</summary>
    public static LicenseAudience Production { get; } = new("production");

    /// <summary>The dev-overlay contour — a license minted by the dev-only Ed25519 key, intended only for non-prod developer deployments.</summary>
    public static LicenseAudience Dev { get; } = new("dev");

    private readonly string? value;

    private LicenseAudience(string value)
    {
        this.value = value;
    }

    /// <summary>Lowercase string form; <see cref="Unspecified"/> is <c>"unspecified"</c>.</summary>
    public string Value => value ?? "unspecified";

    /// <summary>Parses a payload's <c>audience</c> string into a <see cref="LicenseAudience"/>.</summary>
    /// <param name="value">The wire string; compared ordinally against the two known values.</param>
    /// <param name="audience">The matched audience, or <c>default</c> when <paramref name="value"/> is unrecognised.</param>
    /// <returns><c>true</c> when <paramref name="value"/> matches a known audience.</returns>
    public static bool TryParse(string value, out LicenseAudience audience)
    {
        if (string.Equals(value, Production.Value, StringComparison.Ordinal))
        {
            audience = Production;
            return true;
        }

        if (string.Equals(value, Dev.Value, StringComparison.Ordinal))
        {
            audience = Dev;
            return true;
        }

        audience = default;
        return false;
    }
}
