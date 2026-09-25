using Comuki.Shared.Editions.Catalog.Keys;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Editions.Unit;

/// <summary>
/// LimitKey tests — the count-quota axis of edition gating. Shares the
/// well-formedness shape with <see cref="FeatureKey"/>.
/// </summary>
public sealed class LimitKeyShould
{
    [Theory(DisplayName = "Given a well-formed limit key, when IsWellFormed runs, then it returns true")]
    [InlineData("multi-repo")]
    [InlineData("agenteval")]
    [InlineData("a")]
    [InlineData("resource:action")]
    public void WellFormedKeysAreAccepted(string value)
    {
        LimitKey.IsWellFormed(value).ShouldBeTrue();
    }

    [Theory(DisplayName = "Given a malformed limit key, when IsWellFormed runs, then it returns false")]
    [InlineData("")]
    [InlineData("Multi-Repo")]
    [InlineData("multi_repo")]
    [InlineData("a:b:c")]
    [InlineData(":action")]
    [InlineData("resource:")]
    [InlineData("a-very-long-segment-that-exceeds-the-sixty-four-character-cap-on-a-single-segment")]
    public void MalformedKeysAreRejected(string value)
    {
        LimitKey.IsWellFormed(value).ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a well-formed value, when Parse runs, then it returns a LimitKey carrying that value")]
    public void ParseReturnsKey()
    {
        var key = LimitKey.Parse("projects");

        key.Value.ShouldBe("projects");
        key.ToString().ShouldBe("projects");
    }

    [Fact(DisplayName = "Given a malformed value, when Parse runs, then it throws FormatException")]
    public void ParseThrowsForMalformed()
    {
        Should.Throw<FormatException>(
            static () => _ = LimitKey.Parse("BAD_KEY"));
    }
}
