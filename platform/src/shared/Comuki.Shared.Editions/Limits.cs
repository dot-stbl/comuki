using Comuki.Shared.Editions.Catalog;
using Comuki.Shared.Editions.Tiers;

namespace Comuki.Shared.Editions;

/// <summary>
/// The single source of truth for every count-quota key (issue #164 E3).
/// </summary>
public static class Limits
{
    /// <summary>
    /// Project count cap (#163). Community keeps exactly 1 project; the
    /// paid cap is a v1 placeholder — the real SKU-launch number is a
    /// product decision (OQ3), not a spec decision.
    /// </summary>
    public static readonly Limit Projects = Limit.Define(
        "projects",
        "Number of projects a workspace may create.",
        communityValue: 1,
        paidValues: new Dictionary<int, int> { [EditionTiers.Team.Rank] = 10 });

    /// <summary>Every declared limit, sorted by key. Throws at type-init if two entries share a key.</summary>
    public static readonly IReadOnlyList<Limit> All = EditionCatalogGuard.EnsureUniqueSortedByKey(
        [Projects],
        static limit => limit.Key.Value,
        catalogName: "Limits");
}
