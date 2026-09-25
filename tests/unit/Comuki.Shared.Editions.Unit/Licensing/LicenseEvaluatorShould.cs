using Comuki.Shared.Editions.Licensing;
using Comuki.Shared.Editions.Licensing.Audiences;
using Comuki.Shared.Editions.Licensing.Modes;
using Comuki.Shared.Editions.Licensing.Status;
using Comuki.Shared.Editions.Tiers;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Editions.Unit.Licensing;

/// <summary>
/// LicenseEvaluator tests — the pure classifier that turns a
/// (LicenseKey?, now, gracePeriod) triple into a (Status, Tier) pair.
/// </summary>
public sealed class LicenseEvaluatorShould
{
    private static readonly TimeSpan gracePeriod = TimeSpan.FromDays(7);

    private static LicenseKey SampleKey(DateTimeOffset? notBefore = null)
    {
        return new(
            Tier: EditionTiers.Team,
            Org: "Acme Inc",
            NotBefore: notBefore,
            Expiry: new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Mode: LicenseMode.ImplicitByRank,
            Audience: LicenseAudience.Production,
            Features: [],
            Limits: new Dictionary<string, int>(),
            VerifiedAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            VerifiedWith: "0123456789abcdef");
    }

    [Fact(DisplayName = "Given a null license, when Classify runs, then it reports Absent / Community")]
    public void NullLicenseIsAbsentCommunity()
    {
        var result = LicenseEvaluator.Classify(null, new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), gracePeriod);

        result.Status.ShouldBe(LicenseStatus.Absent);
        result.Current.ShouldBe(EditionTier.Community);
    }

    [Fact(DisplayName = "Given a license with NotBefore in the future, when Classify runs, then it reports Absent / Community")]
    public void FutureNotBeforeIsAbsentCommunity()
    {
        var key = SampleKey(notBefore: new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var now = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

        var result = LicenseEvaluator.Classify(key, now, gracePeriod);

        result.Status.ShouldBe(LicenseStatus.Absent);
        result.Current.ShouldBe(EditionTier.Community);
    }

    [Fact(DisplayName = "Given a license with no NotBefore and now before Expiry, when Classify runs, then it reports Valid / license tier")]
    public void InsideWindowIsValid()
    {
        var key = SampleKey();
        var now = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

        var result = LicenseEvaluator.Classify(key, now, gracePeriod);

        result.Status.ShouldBe(LicenseStatus.Valid);
        result.Current.ShouldBe(EditionTiers.Team);
    }

    [Fact(DisplayName = "Given now just past Expiry but inside the grace period, when Classify runs, then it reports Grace / license tier")]
    public void InsideGraceIsGrace()
    {
        var key = SampleKey();
        var now = new DateTimeOffset(2027, 1, 4, 0, 0, 0, TimeSpan.Zero);

        var result = LicenseEvaluator.Classify(key, now, gracePeriod);

        result.Status.ShouldBe(LicenseStatus.Grace);
        result.Current.ShouldBe(EditionTiers.Team);
    }

    [Fact(DisplayName = "Given now past Expiry + gracePeriod, when Classify runs, then it reports Expired but Current stays at the licensed tier")]
    public void PastGraceIsExpiredButKeepsPaidTier()
    {
        var key = SampleKey();
        var now = new DateTimeOffset(2027, 2, 1, 0, 0, 0, TimeSpan.Zero);

        var result = LicenseEvaluator.Classify(key, now, gracePeriod);

        result.Status.ShouldBe(LicenseStatus.Expired);
        result.Current.ShouldBe(EditionTiers.Team);
    }

    [Fact(DisplayName = "Given a negative gracePeriod, when Classify runs, then ArgumentOutOfRangeException is thrown")]
    public void NegativeGraceThrows()
    {
        var key = SampleKey();
        var now = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

        Should.Throw<ArgumentOutOfRangeException>(
            () => LicenseEvaluator.Classify(key, now, TimeSpan.FromDays(-1)));
    }
}
