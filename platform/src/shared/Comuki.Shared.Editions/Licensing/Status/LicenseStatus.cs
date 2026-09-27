namespace Comuki.Shared.Editions.Licensing.Status;

/// <summary>
/// The lifecycle classification of a verified <see cref="LicenseKey"/>
/// at a given instant — produced only by <see cref="LicenseEvaluator"/>,
/// never read from the wire. A smart type, not an enum
/// (anti-patterns.md §1.4 / smart-types.md §2): the four states are
/// domain-defined, not protocol-defined.
/// </summary>
public readonly record struct LicenseStatus
{
    /// <summary>
    /// The default value (<c>default(LicenseStatus)</c>). Never a
    /// legal status — observing it is a classification invariant
    /// violation, not a silent fifth option.
    /// </summary>
    public static LicenseStatus Unspecified { get; }

    /// <summary>No license at all, or a license whose <see cref="LicenseKey.NotBefore"/> is still in the future.</summary>
    public static LicenseStatus Absent { get; } = new("absent");

    /// <summary>The license is inside its validity window.</summary>
    public static LicenseStatus Valid { get; } = new("valid");

    /// <summary>The license is past <see cref="LicenseKey.Expiry"/> but inside the configured grace period.</summary>
    public static LicenseStatus Grace { get; } = new("grace");

    /// <summary>The license is past <see cref="LicenseKey.Expiry"/> + grace period.</summary>
    public static LicenseStatus Expired { get; } = new("expired");

    private readonly string? value;

    private LicenseStatus(string value)
    {
        this.value = value;
    }

    /// <summary>Lowercase string form; <see cref="Unspecified"/> is <c>"unspecified"</c>.</summary>
    public string Value => value ?? "unspecified";
}
