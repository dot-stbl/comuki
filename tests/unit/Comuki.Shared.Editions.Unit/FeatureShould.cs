using Comuki.Shared.Editions.Catalog;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Editions.Unit;

/// <summary>
/// Feature tests — the catalog entry that pairs a well-formed key with
/// the smallest tier rank at which it unlocks.
/// </summary>
public sealed class FeatureShould
{
    [Fact(DisplayName = "Given a well-formed key, non-empty description, and non-negative rank, when Define runs, then properties round-trip")]
    public void DefineRoundTrips()
    {
        var feature = Feature.Define("multi-repo", "Multi-repo workspaces.", minimumRank: 1);

        feature.Key.Value.ShouldBe("multi-repo");
        feature.Description.ShouldBe("Multi-repo workspaces.");
        feature.MinimumRank.ShouldBe(1);
        feature.SinceUnixMs.ShouldBeNull();
    }

    [Fact(DisplayName = "Given an explicit sinceUnixMs, when Define runs, then it is preserved on the entry")]
    public void DefinePreservesSinceUnixMs()
    {
        var feature = Feature.Define("agenteval", "Agent eval dashboard.", minimumRank: 1, sinceUnixMs: 1_700_000_000_000L);

        feature.SinceUnixMs.ShouldBe(1_700_000_000_000L);
    }

    [Fact(DisplayName = "Given an empty description, when Define runs, then ArgumentException is thrown")]
    public void EmptyDescriptionThrows()
    {
        Should.Throw<ArgumentException>(
            static () => Feature.Define("multi-repo", string.Empty, minimumRank: 1));
    }

    [Fact(DisplayName = "Given a whitespace description, when Define runs, then ArgumentException is thrown")]
    public void WhitespaceDescriptionThrows()
    {
        Should.Throw<ArgumentException>(
            static () => Feature.Define("multi-repo", "   ", minimumRank: 1));
    }

    [Fact(DisplayName = "Given a negative minimum rank, when Define runs, then ArgumentOutOfRangeException is thrown")]
    public void NegativeRankThrows()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            static () => Feature.Define("multi-repo", "Multi-repo workspaces.", minimumRank: -1));
    }

    [Fact(DisplayName = "Given a malformed key, when Define runs, then FormatException is thrown")]
    public void MalformedKeyThrows()
    {
        Should.Throw<FormatException>(
            static () => Feature.Define("BAD_KEY", "Anything non-empty.", minimumRank: 1));
    }
}
