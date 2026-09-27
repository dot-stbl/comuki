using Comuki.Shared.Editions.Gating;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Editions.Unit.Gating.Attributes;

/// <summary>
/// RequiresFeatureAttribute tests — the metadata-only carrier that the
/// filter / middleware read for the demanded feature key. The attribute
/// has no logic of its own; the tests below lock the round-trip shape so
/// the filter / middleware never have to second-guess the property name.
/// </summary>
public sealed class RequiresFeatureAttributeShould
{
    [Fact(DisplayName = "Given a feature key, when the attribute is constructed, then FeatureKey round-trips verbatim")]
    public void FeatureKeyRoundTrips()
    {
        var attribute = new RequiresFeatureAttribute("multi-repo");

        attribute.FeatureKey.ShouldBe("multi-repo");
    }

    [Fact(DisplayName = "Given two attributes constructed with the same key, when they are compared, then they are not the same instance")]
    public void ConstructedTwiceProducesDistinctInstances()
    {
        var first = new RequiresFeatureAttribute("agenteval");
        var second = new RequiresFeatureAttribute("agenteval");

        first.ShouldNotBeSameAs(second);
        first.FeatureKey.ShouldBe(second.FeatureKey);
    }
}
