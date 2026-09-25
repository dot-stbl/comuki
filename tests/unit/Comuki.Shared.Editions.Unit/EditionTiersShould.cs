using Comuki.Shared.Editions.Tiers;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Editions.Unit;

/// <summary>
/// EditionTiers tests — the static catalog the license verifier resolves
/// a license's <c>edition</c> code against.
/// </summary>
public sealed class EditionTiersShould
{
    [Fact(DisplayName = "Given the 'community' code, when TryGetByCode runs, then it hits with rank 0")]
    public void CommunityCodeResolves()
    {
        var hit = EditionTiers.TryGetByCode("community", out var tier);

        hit.ShouldBeTrue();
        tier.Rank.ShouldBe(0);
        tier.Code.ShouldBe("community");
    }

    [Fact(DisplayName = "Given the 'team' code, when TryGetByCode runs, then it hits with rank 1")]
    public void TeamCodeResolves()
    {
        var hit = EditionTiers.TryGetByCode("team", out var tier);

        hit.ShouldBeTrue();
        tier.Rank.ShouldBe(1);
        tier.Code.ShouldBe("team");
    }

    [Fact(DisplayName = "Given an unknown code, when TryGetByCode runs, then it misses and the out parameter is default")]
    public void UnknownCodeMisses()
    {
        var hit = EditionTiers.TryGetByCode("nonexistent", out var tier);

        hit.ShouldBeFalse();
        tier.ShouldBe(default);
    }

    [Fact(DisplayName = "Given the Tier constants, when the catalog is enumerated, then Community is listed before Team")]
    public void CommunityAppearsBeforeTeam()
    {
        EditionTiers.All[0].ShouldBe(EditionTiers.Community);
        EditionTiers.All[1].ShouldBe(EditionTiers.Team);
    }
}
