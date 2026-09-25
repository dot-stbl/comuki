using Comuki.Shared.Editions.Catalog.Keys;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Editions.Unit;

/// <summary>
/// FeatureKey tests — the closed capability-key axis of edition gating.
/// </summary>
public sealed class FeatureKeyShould
{
    [Theory(DisplayName = "Given a well-formed feature key, when IsWellFormed runs, then it returns true")]
    [InlineData("multi-repo")]
    [InlineData("agenteval")]
    [InlineData("a")]
    [InlineData("resource:action")]
    public void WellFormedKeysAreAccepted(string value)
    {
        FeatureKey.IsWellFormed(value).ShouldBeTrue();
    }

    [Theory(DisplayName = "Given a malformed feature key, when IsWellFormed runs, then it returns false")]
    [InlineData("")]
    [InlineData("Multi-Repo")]
    [InlineData("multi_repo")]
    [InlineData("a:b:c")]
    [InlineData(":action")]
    [InlineData("resource:")]
    [InlineData("a-very-long-segment-that-exceeds-the-sixty-four-character-cap-on-a-single-segment")]
    public void MalformedKeysAreRejected(string value)
    {
        FeatureKey.IsWellFormed(value).ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a well-formed value, when Parse runs, then it returns a FeatureKey carrying that value")]
    public void ParseReturnsKey()
    {
        var key = FeatureKey.Parse("multi-repo");

        key.Value.ShouldBe("multi-repo");
        key.ToString().ShouldBe("multi-repo");
    }

    [Fact(DisplayName = "Given a malformed value, when Parse runs, then it throws FormatException")]
    public void ParseThrowsForMalformed()
    {
        Should.Throw<FormatException>(
            static () => FeatureKey.Parse("BAD_KEY"));
    }
}
