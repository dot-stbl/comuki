using Comuki.Shared.Editions.Tiers;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Editions.Unit;

/// <summary>
/// EditionTier tests — the closed ordinal rank + open string code the
/// capability registry compares coverage against.
/// </summary>
public sealed class EditionTierShould
{
    [Fact(DisplayName = "Given the Community constant, when its rank and code are read, then they are 0 and 'community'")]
    public void CommunityIsRankZero()
    {
        EditionTier.Community.Rank.ShouldBe(0);
        EditionTier.Community.Code.ShouldBe("community");
    }

    [Fact(DisplayName = "Given a negative rank, when constructing a tier, then ArgumentOutOfRangeException is thrown")]
    public void NegativeRankThrows()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            static () => new EditionTier(-1, "x"));
    }

    [Fact(DisplayName = "Given an empty code, when constructing a tier, then ArgumentException is thrown")]
    public void EmptyCodeThrows()
    {
        Should.Throw<ArgumentException>(
            static () => new EditionTier(0, ""));
    }

    [Fact(DisplayName = "Given a whitespace code, when constructing a tier, then ArgumentException is thrown")]
    public void WhitespaceCodeThrows()
    {
        Should.Throw<ArgumentException>(
            static () => new EditionTier(0, "   "));
    }

    [Fact(DisplayName = "Given a non-negative rank and a non-empty code, when constructing a tier, then the rank and code round-trip")]
    public void RoundTrips()
    {
        var tier = new EditionTier(2, "enterprise");

        tier.Rank.ShouldBe(2);
        tier.Code.ShouldBe("enterprise");
    }
}
