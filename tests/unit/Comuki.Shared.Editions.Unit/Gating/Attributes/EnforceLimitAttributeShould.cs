using Comuki.Shared.Editions.Gating;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Editions.Unit.Gating.Attributes;

/// <summary>
/// EnforceLimitAttribute tests — the metadata-only carrier that the
/// filter / middleware read for the demanded limit key. The attribute
/// has no logic of its own; the tests below lock the round-trip shape so
/// the filter / middleware never have to second-guess the property name.
/// </summary>
public sealed class EnforceLimitAttributeShould
{
    [Fact(DisplayName = "Given a limit key, when the attribute is constructed, then LimitKey round-trips verbatim")]
    public void LimitKeyRoundTrips()
    {
        var attribute = new EnforceLimitAttribute("projects");

        attribute.LimitKey.ShouldBe("projects");
    }

    [Fact(DisplayName = "Given two attributes constructed with the same key, when they are compared, then they are not the same instance")]
    public void ConstructedTwiceProducesDistinctInstances()
    {
        var first = new EnforceLimitAttribute("projects");
        var second = new EnforceLimitAttribute("projects");

        first.ShouldNotBeSameAs(second);
        first.LimitKey.ShouldBe(second.LimitKey);
    }
}
