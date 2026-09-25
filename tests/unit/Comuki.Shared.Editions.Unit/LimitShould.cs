using Comuki.Shared.Editions.Catalog;
using Comuki.Shared.Editions.Tiers;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Editions.Unit;

/// <summary>
/// Limit tests — a count-quota entry with a Community cap and optional
/// per-rank paid overrides. Unlike <see cref="Feature"/>, a limit always
/// applies (Community included) — only its numeric cap changes by tier.
/// </summary>
public sealed class LimitShould
{
    [Fact(DisplayName = "Given no paid overrides, when Define runs, then properties round-trip")]
    public void DefineWithoutPaidValuesRoundTrips()
    {
        var limit = Limit.Define("projects", "Number of projects.", communityValue: 1);

        limit.Key.Value.ShouldBe("projects");
        limit.Description.ShouldBe("Number of projects.");
        limit.CommunityValue.ShouldBe(1);
        limit.PaidValues.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given paid overrides, when Define runs, then they are preserved on the entry")]
    public void DefineWithPaidValuesRoundTrips()
    {
        var limit = Limit.Define(
            "projects",
            "Number of projects.",
            communityValue: 1,
            paidValues: new Dictionary<int, int> { [EditionTiers.Team.Rank] = 10 });

        limit.PaidValues.ShouldContainKey(EditionTiers.Team.Rank);
        limit.PaidValues[EditionTiers.Team.Rank].ShouldBe(10);
    }

    [Fact(DisplayName = "Given an empty description, when Define runs, then ArgumentException is thrown")]
    public void EmptyDescriptionThrows()
    {
        Should.Throw<ArgumentException>(
            static () => _ = Limit.Define("projects", string.Empty, communityValue: 1));
    }

    [Fact(DisplayName = "Given a paidValues rank of 0, when Define runs, then ArgumentOutOfRangeException is thrown (rank 0 is CommunityValue)")]
    public void PaidValuesRankZeroThrows()
    {
        var paid = new Dictionary<int, int> { [0] = 99 };

        Should.Throw<ArgumentOutOfRangeException>(
            () => _ = Limit.Define("projects", "desc", communityValue: 1, paidValues: paid));
    }

    [Fact(DisplayName = "Given a negative paidValues rank, when Define runs, then ArgumentOutOfRangeException is thrown")]
    public void NegativePaidValuesRankThrows()
    {
        var paid = new Dictionary<int, int> { [-1] = 99 };

        Should.Throw<ArgumentOutOfRangeException>(
            () => _ = Limit.Define("projects", "desc", communityValue: 1, paidValues: paid));
    }

    [Fact(DisplayName = "Given a tier whose rank has no paid override, when ValueFor runs, then CommunityValue is returned")]
    public void ValueForReturnsCommunityWhenNoPaidOverride()
    {
        var limit = Limit.Define(
            "projects",
            "Number of projects.",
            communityValue: 1,
            paidValues: new Dictionary<int, int> { [2] = 50 });

        limit.ValueFor(EditionTier.Community).ShouldBe(1);
        limit.ValueFor(new EditionTier(1, "between")).ShouldBe(1);
    }

    [Fact(DisplayName = "Given a tier whose rank has a paid override, when ValueFor runs, then the exact override is returned")]
    public void ValueForReturnsExactMatch()
    {
        var limit = Limit.Define(
            "projects",
            "Number of projects.",
            communityValue: 1,
            paidValues: new Dictionary<int, int> { [EditionTiers.Team.Rank] = 10 });

        limit.ValueFor(EditionTiers.Team).ShouldBe(10);
    }

    [Fact(DisplayName = "Given a tier whose rank is above every paid override, when ValueFor runs, then the highest lower-or-equal override is returned")]
    public void ValueForReturnsHighestLowerOrEqualOverride()
    {
        var limit = Limit.Define(
            "projects",
            "Number of projects.",
            communityValue: 1,
            paidValues: new Dictionary<int, int> { [EditionTiers.Team.Rank] = 10 });

        limit.ValueFor(new EditionTier(2, "above-team")).ShouldBe(10);
    }
}
