namespace Comuki.Engine.Compute.Environments.Shape;

/// <summary>
/// Closed set of marketplace shelves a bundle may sit on
/// (add-worker-environments 6.2; spec scenario "Marketplace shelves
/// and fleet allowlist"). The shelf is the operator-facing identity a
/// bundle declares; the fleet allowlist is the operator decision of
/// which shelves (or, more narrowly, which publisher names within
/// those shelves) the workers pull. Mirrors
/// <see cref="EnvironmentPublisher" /> but for the admission question:
/// the publisher names who shipped the bundle; the shelf is the row in
/// the operator console the admission gate reads against.
/// </summary>
public readonly record struct EnvironmentShelf
{
    private const string UnspecifiedValue = "unspecified";

    private readonly string? value;

    private EnvironmentShelf(string value)
    {
        this.value = value;
    }

    /// <summary>Wire value; <c>"unspecified"</c> for <see cref="Unspecified" />.</summary>
    public string Value => value ?? UnspecifiedValue;

    /// <summary>Value of <c>default(EnvironmentShelf)</c>. Not a shelf.</summary>
    public static EnvironmentShelf Unspecified { get; }

    /// <summary>Comuki shelf — the golden classes and any future Comuki-published class.</summary>
    public static EnvironmentShelf Comuki { get; } = new("comuki");

    /// <summary>Organization shelf — classes the tenant organisation publishes for its own fleet.</summary>
    public static EnvironmentShelf Org { get; } = new("org");

    /// <summary>Community shelf — third-party classes; require a fleet allowlist match to resolve.</summary>
    public static EnvironmentShelf Community { get; } = new("community");
}
