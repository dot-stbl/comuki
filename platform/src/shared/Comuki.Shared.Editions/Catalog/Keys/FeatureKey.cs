namespace Comuki.Shared.Editions.Catalog.Keys;

/// <summary>
/// A feature key — the closed capability-key axis of edition gating,
/// declared in code (<see cref="Features"/>). Distinct from the
/// Identity module's permission keys: edition gating and permission
/// gating are independent axes.
/// </summary>
/// <param name="Value">The key, e.g. <c>multi-repo</c>.</param>
public readonly record struct FeatureKey(string Value)
{
    /// <summary>Whether <paramref name="value"/> has the well-formed feature-key shape.</summary>
    public static bool IsWellFormed(string value)
    {
        return CatalogKeyShape.IsWellFormed(value);
    }

    /// <summary>Parses a well-formed key; throws for anything else.</summary>
    /// <exception cref="FormatException"><paramref name="value"/> is not well-formed.</exception>
    public static FeatureKey Parse(string value)
    {
        return IsWellFormed(value)
        ? new FeatureKey(value)
        : throw new FormatException($"'{value}' is not a well-formed feature key.");
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Value;
    }
}
