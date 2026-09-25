using Comuki.Shared.Editions;
using Comuki.Shared.Editions.Tiers;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Editions.Unit;

/// <summary>
/// Limits catalog tests — the single source of truth for every count
/// quota key.
/// </summary>
public sealed class LimitsCatalogShould
{
    [Fact(DisplayName = "Given the Limits catalog today, when All is enumerated, then it has exactly one entry (Projects)")]
    public void CatalogHasOneEntry()
    {
        Limits.All.Count.ShouldBe(1);
        Limits.All[0].Key.Value.ShouldBe("projects");
    }

    [Fact(DisplayName = "Given the Projects limit, when CommunityValue is read, then it equals 1")]
    public void ProjectsCommunityValueIsOne()
    {
        Limits.Projects.CommunityValue.ShouldBe(1);
    }

    [Fact(DisplayName = "Given the Projects limit, when PaidValues is read, then it contains a Team-rank override")]
    public void ProjectsHasTeamPaidOverride()
    {
        Limits.Projects.PaidValues.ShouldContainKey(EditionTiers.Team.Rank);
    }
}
