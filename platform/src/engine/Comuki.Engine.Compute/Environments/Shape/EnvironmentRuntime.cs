namespace Comuki.Engine.Compute.Environments.Shape;

/// <summary>
/// Closed set of runtimes a catalog entry can target. The wire form is the
/// lower-case string of <c>.comuki/environment.toml</c> (<c>linux</c>,
/// <c>windows</c>); the catalog stores this typed value so the gate that
/// decides whether a class is startable on a given host compares values
/// without parsing (smart-type, per the domain-types canon — an enum would
/// admit <c>(EnvironmentRuntime)47</c> through the back door).
/// </summary>
public readonly record struct EnvironmentRuntime
{
    private const string UnspecifiedValue = "unspecified";

    private readonly string? value;

    private EnvironmentRuntime(string value)
    {
        this.value = value;
    }

    /// <summary>Wire value; <c>"unspecified"</c> for <see cref="Unspecified" />.</summary>
    public string Value => value ?? UnspecifiedValue;

    /// <summary>Value of <c>default(EnvironmentRuntime)</c>. Not a working runtime.</summary>
    public static EnvironmentRuntime Unspecified { get; }

    /// <summary>Linux — the Comuki golden classes and most community classes.</summary>
    public static EnvironmentRuntime Linux { get; } = new("linux");

    /// <summary>Windows — UE / WPF / native-Windows toolchains.</summary>
    public static EnvironmentRuntime Windows { get; } = new("windows");
}
