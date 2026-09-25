using Comuki.Shared.Editions.Licensing.Modes;
using Comuki.Shared.Editions.Tiers;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Editions.Unit.Fixtures;

/// <summary>
/// Tests that the <see cref="TestLicense"/> fixture itself is wired up
/// correctly. Every test in the suite that uses the fixture depends on
/// these properties: the keypair signs successfully, the verifier
/// round-trips the issuer's claims, and the convenience overload picks
/// the right tier for an allowlist claim.
/// </summary>
public sealed class TestLicenseShould
{
    [Fact(DisplayName = "Given TestLicense.Community, when verified by TestLicense.Provider, then Tier is EditionTiers.Community")]
    public void CommunityTokenVerifiesAsCommunityTier()
    {
        var key = TestLicense.Provider.Verify(TestLicense.Community);

        key.Tier.ShouldBe(EditionTiers.Community);
        key.Org.ShouldBe("Test Org");
    }

    [Fact(DisplayName = "Given TestLicense.With(EditionTiers.Team, limits: { projects = 40 }), when verified, then Limits contains projects = 40")]
    public void LimitsParameterRoundTripsThroughVerification()
    {
        var token = TestLicense.With(EditionTiers.Team, limits: new Dictionary<string, int> { ["projects"] = 40 });

        var key = TestLicense.Provider.Verify(token);

        key.Tier.ShouldBe(EditionTiers.Team);
        key.Limits.ShouldContainKey("projects");
        key.Limits["projects"].ShouldBe(40);
    }

    [Fact(DisplayName = "Given TestLicense.With(Features.MultiRepo), when verified, then Mode is ExplicitAllowlist and Features contains the multi-repo key")]
    public void FeatureOverloadProducesExplicitAllowlistWithRightFeatures()
    {
        var token = TestLicense.With(Features.MultiRepo);

        var key = TestLicense.Provider.Verify(token);

        key.Mode.ShouldBe(LicenseMode.ExplicitAllowlist);
        key.Features.ShouldContain("multi-repo");
    }
}
