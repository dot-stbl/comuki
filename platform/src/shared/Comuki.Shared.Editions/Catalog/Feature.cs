using Comuki.Shared.Editions.Catalog.Keys;

namespace Comuki.Shared.Editions.Catalog;

/// <summary>
/// One entry in the <see cref="Editions.Features"/> registry: a paid
/// capability key and the smallest tier rank that unlocks it. Built only
/// through <see cref="Define"/> — never a bare string constant (issue
/// #164 E9).
/// </summary>
public sealed record Feature
{
    /// <summary>Declares a registry entry. <paramref name="key"/> must be well-formed (<see cref="FeatureKey.IsWellFormed"/>).</summary>
    /// <exception cref="FormatException"><paramref name="key"/> is not well-formed.</exception>
    /// <exception cref="ArgumentException"><paramref name="description"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="minimumRank"/> is negative.</exception>
    public static Feature Define(string key, string description, int minimumRank, long? sinceUnixMs = null)
    {
        return new Feature(
            FeatureKey.Parse(key),
            string.IsNullOrWhiteSpace(description)
                ? throw new ArgumentException("Feature description cannot be empty.", nameof(description))
                : description,
            minimumRank is < 0
                ? throw new ArgumentOutOfRangeException(nameof(minimumRank), minimumRank, "Feature minimum rank cannot be negative.")
                : minimumRank,
            sinceUnixMs);
    }

    private Feature(FeatureKey key, string description, int minimumRank, long? sinceUnixMs)
    {
        Key = key;
        Description = description;
        MinimumRank = minimumRank;
        SinceUnixMs = sinceUnixMs;
    }

    /// <summary>The well-formed, unique key.</summary>
    public FeatureKey Key { get; }

    /// <summary>Human-readable description (feeds the generated capability table — codegen is a follow-up change).</summary>
    public string Description { get; }

    /// <summary>The smallest <see cref="Tiers.EditionTier.Rank"/> at which this feature is available.</summary>
    public int MinimumRank { get; }

    /// <summary>Optional Unix-ms timestamp of the first tier this feature became available (informational).</summary>
    public long? SinceUnixMs { get; }
}
