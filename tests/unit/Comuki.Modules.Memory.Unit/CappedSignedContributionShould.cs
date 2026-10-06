using Comuki.Modules.Memory.Application.Ranking;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Memory.Unit;

/// <summary>
/// The capped signed contribution is the single number the learning loop
/// maintains for a project under a candidate rule. Tests lock the
/// behaviour the loop depends on: <c>[-cap, +cap]</c> clamping, default
/// cap of 5, and the symmetric treatment of success and failure.
/// </summary>
public sealed class CappedSignedContributionShould
{
    [Fact(DisplayName = "Given a zero current, when a positive delta is applied, then the result is the delta")]
    public void PositiveDeltaFromZeroReturnsDelta()
    {
        CappedSignedContribution.Apply(0, 3).ShouldBe(3);
    }

    [Fact(DisplayName = "Given a zero current, when a negative delta is applied, then the result is the delta")]
    public void NegativeDeltaFromZeroReturnsDelta()
    {
        CappedSignedContribution.Apply(0, -2).ShouldBe(-2);
    }

    [Fact(DisplayName = "Given a current below the positive cap, when a positive delta pushes over, then the result is capped at +cap")]
    public void PositiveDeltaClampsAtPositiveCap()
    {
        CappedSignedContribution.Apply(4, 4, cap: 5).ShouldBe(5);
    }

    [Fact(DisplayName = "Given a current below the negative cap, when a negative delta pushes under, then the result is clamped at -cap")]
    public void NegativeDeltaClampsAtNegativeCap()
    {
        CappedSignedContribution.Apply(-3, -4, cap: 5).ShouldBe(-5);
    }

    [Fact(DisplayName = "Given the default cap, when a delta of 100 is applied, then the result is the default cap")]
    public void DefaultCapClampsLargePositiveDelta()
    {
        CappedSignedContribution.Apply(0, 100).ShouldBe(CappedSignedContribution.DefaultCap);
    }

    [Fact(DisplayName = "Given the default cap, when a delta of -100 is applied, then the result is the negated default cap")]
    public void DefaultCapClampsLargeNegativeDelta()
    {
        CappedSignedContribution.Apply(0, -100).ShouldBe(-CappedSignedContribution.DefaultCap);
    }

    [Fact(DisplayName = "Given a current at the positive cap, when a positive delta is applied, then the result stays at the cap")]
    public void AlreadyAtCapStaysAtCapOnFurtherPositiveDelta()
    {
        CappedSignedContribution.Apply(5, 1, cap: 5).ShouldBe(5);
    }

    [Fact(DisplayName = "Given a current at the negative cap, when a negative delta is applied, then the result stays at the negative cap")]
    public void AlreadyAtNegativeCapStaysOnFurtherNegativeDelta()
    {
        CappedSignedContribution.Apply(-5, -1, cap: 5).ShouldBe(-5);
    }

    [Fact(DisplayName = "Given a null cap, when applied, then the default cap is used")]
    public void NullCapFallsBackToDefault()
    {
        CappedSignedContribution.Apply(0, 100, cap: null).ShouldBe(CappedSignedContribution.DefaultCap);
    }

    [Fact(DisplayName = "Given a cap of zero, when any non-zero delta is applied, then the result is zero")]
    public void ZeroCapClampsToZero()
    {
        CappedSignedContribution.Apply(0, 5, cap: 0).ShouldBe(0);
        CappedSignedContribution.Apply(0, -5, cap: 0).ShouldBe(0);
    }

    [Fact(DisplayName = "Given a negative cap, when applied, then the absolute value of the cap is used")]
    public void NegativeCapIsTakenAsAbsoluteValue()
    {
        CappedSignedContribution.Apply(0, 100, cap: -3).ShouldBe(3);
        CappedSignedContribution.Apply(0, -100, cap: -3).ShouldBe(-3);
    }

    [Fact(DisplayName = "Given a current that crosses zero from below, when a small positive delta is applied, then the result is exactly the new value")]
    public void CrossingZeroUsesTheNewValue()
    {
        CappedSignedContribution.Apply(-2, 3, cap: 5).ShouldBe(1);
    }
}
