using Comuki.Shared.Editions.Tiers;

namespace Comuki.Shared.Editions.Catalog.Keys;

/// <summary>
/// Resolves a <see cref="Feature.MinimumRank"/> into its
/// <see cref="EditionTier.Code"/>; null when no tier carries that rank.
/// Pure helper — no I/O, no DI; lifted out of <c>EditionGate</c>'s
/// private statics so every call site has the same tier-code vocabulary
/// (class-layout-and-tooling §1a — no private methods on production
/// classes).
/// </summary>
internal static class TierCodes
{
    /// <summary>
    /// Resolves <paramref name="minimumRank"/> to its tier code; null when
    /// no <see cref="EditionTiers.All"/> entry carries the rank.
    /// </summary>
    /// <param name="minimumRank">The minimum rank demanded by a feature.</param>
    /// <returns>The matching tier code, or null.</returns>
    public static string? RankToCode(int minimumRank)
    {
        foreach (var tier in EditionTiers.All)
        {
            if (tier.Rank == minimumRank)
            {
                return tier.Code;
            }
        }

        return null;
    }
}
