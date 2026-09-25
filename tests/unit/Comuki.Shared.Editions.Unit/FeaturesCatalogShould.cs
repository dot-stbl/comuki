using Comuki.Shared.Editions.Catalog;
using Comuki.Shared.Editions.Tiers;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Editions.Unit;

/// <summary>
/// Features catalog tests — the single source of truth for every paid
/// capability key, asserted at type-init by
/// <see cref="EditionCatalogGuard"/>.
/// </summary>
public sealed class FeaturesCatalogShould
{
    [Fact(DisplayName = "Given the Features catalog, when All is enumerated, then it has exactly eight entries")]
    public void CatalogHasEightEntries()
    {
        Features.All.Count.ShouldBe(8);
    }

    [Fact(DisplayName = "Given the Features catalog, when All is enumerated, then every entry's Key.Value is unique")]
    public void CatalogKeysAreUnique()
    {
        var keys = Features.All.Select(static feature => feature.Key.Value).ToList();

        keys.Count.ShouldBe(keys.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact(DisplayName = "Given the Features catalog, when All is enumerated, then it is sorted ascending by Key.Value (ordinal)")]
    public void CatalogIsSortedByKey()
    {
        Features.All
            .Select(static feature => feature.Key.Value)
            .ShouldBe(Features.All.Select(static feature => feature.Key.Value).OrderBy(static value => value, StringComparer.Ordinal));
    }

    [Fact(DisplayName = "Given the Features catalog today, when All is enumerated, then every entry's MinimumRank equals EditionTiers.Team.Rank")]
    public void EveryEntryGatedAtTeam()
    {
        foreach (var feature in Features.All)
        {
            feature.MinimumRank.ShouldBe(EditionTiers.Team.Rank);
        }
    }
}
