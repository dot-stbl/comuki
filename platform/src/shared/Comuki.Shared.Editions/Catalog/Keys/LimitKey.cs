namespace Comuki.Shared.Editions.Catalog.Keys;

/// <summary>
/// A limit key — the count-quota axis of edition gating, declared in code
/// (<see cref="Editions.Limits"/>).
/// </summary>
/// <param name="Value">The key, e.g. <c>projects</c>.</param>
public readonly record struct LimitKey(string Value)
{
    /// <summary>Whether <paramref name="value"/> has the well-formed limit-key shape.</summary>
    public static bool IsWellFormed(string value) => CatalogKeyShape.IsWellFormed(value);

    /// <summary>Parses a well-formed key; throws for anything else.</summary>
    /// <exception cref="FormatException"><paramref name="value"/> is not well-formed.</exception>
    public static LimitKey Parse(string value) => IsWellFormed(value)
        ? new LimitKey(value)
        : throw new FormatException($"'{value}' is not a well-formed limit key.");

    /// <inheritdoc />
    public override string ToString() => Value;
}
