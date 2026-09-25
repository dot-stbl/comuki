namespace Comuki.Shared.Editions.Tiers;

/// <summary>
/// Every tier code the platform recognises. A license names a tier by
/// <see cref="EditionTier.Code"/>; <see cref="TryGetByCode"/> is how the
/// license verifier (a later chunk) resolves that code to its
/// <see cref="EditionTier.Rank"/> — an unrecognised code is a malformed
/// license. Adding a third tier (OQ3) is appending a row here, never
/// editing a closed enum.
/// </summary>
public static class EditionTiers
{
    /// <summary>Rank 0 — the default, no license required.</summary>
    public static readonly EditionTier Community = EditionTier.Community;

    /// <summary>
    /// Rank 1 — the first paid tier. <c>"team"</c> is a v1 placeholder
    /// code (OQ3): the real SKU-launch naming is a product decision
    /// tracked as a follow-up, not a spec decision.
    /// </summary>
    public static readonly EditionTier Team = new(1, "team");

    /// <summary>Every recognised tier, Community first.</summary>
    public static readonly IReadOnlyList<EditionTier> All = [Community, Team];

    /// <summary>Resolves a license's tier code to its <see cref="EditionTier"/>.</summary>
    /// <param name="code">The tier code as it appears in a license payload's <c>edition</c> field.</param>
    /// <param name="tier">The matching tier, or <c>default</c> when <paramref name="code"/> is not recognised.</param>
    /// <returns><c>true</c> when <paramref name="code"/> matches a known tier.</returns>
    public static bool TryGetByCode(string code, out EditionTier tier)
    {
        foreach (var candidate in All)
        {
            if (string.Equals(candidate.Code, code, StringComparison.Ordinal))
            {
                tier = candidate;
                return true;
            }
        }

        tier = default;
        return false;
    }
}
