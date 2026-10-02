namespace Comuki.Engine.Compute.Environments.Shape;

/// <summary>
/// Closed set of publisher shelves a catalog entry can carry. The fleet
/// allowlist (task 6.2 — out of scope for this slice) gates non-comuki
/// publishers; the catalog stores this typed value so the allowlist gate
/// compares values without parsing strings (smart-type per the
/// domain-types canon).
/// </summary>
public readonly record struct EnvironmentPublisher
{
    private const string UnspecifiedValue = "unspecified";

    private readonly string? value;

    private EnvironmentPublisher(string value)
    {
        this.value = value;
    }

    /// <summary>Wire value; <c>"unspecified"</c> for <see cref="Unspecified" />.</summary>
    public string Value => value ?? UnspecifiedValue;

    /// <summary>Value of <c>default(EnvironmentPublisher)</c>. Not a shelf.</summary>
    public static EnvironmentPublisher Unspecified { get; }

    /// <summary>Comuki shelf — the golden classes and any future Comuki-published class.</summary>
    public static EnvironmentPublisher Comuki { get; } = new("comuki");

    /// <summary>Organization shelf — classes the tenant organisation publishes for its own fleet.</summary>
    public static EnvironmentPublisher Org { get; } = new("org");

    /// <summary>Community shelf — third-party classes; require a fleet allowlist match to resolve (worker-environments spec §"Marketplace shelves and fleet allowlist").</summary>
    public static EnvironmentPublisher Community { get; } = new("community");
}
