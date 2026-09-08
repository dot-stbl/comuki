using Comuki.Modules.Proxy.Application.Budgeting;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Proxy.Unit;

/// <summary>
/// Pure date-arithmetic helpers for the proxy budget enforcer: month
/// boundary detection, month-length math, USD → micros conversion, and the
/// content-length → token heuristic used by the request guard. Pure
/// functions only — the budget enforcer is the only consumer.
/// </summary>
public sealed class ProxyBudgetMathShould
{
    [Fact(DisplayName = "Given an instant mid-month, when StartOfMonth runs, then the result is the first day of that month at 00:00 in the same offset")]
    public void StartOfMonthReturnsFirstInstantOfCalendarMonth()
    {
        var instant = new DateTimeOffset(2026, 9, 8, 12, 34, 56, TimeSpan.FromHours(5));
        var expected = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.FromHours(5));

        var start = ProxyBudgetMath.StartOfMonth(instant);

        start.ShouldBe(expected);
    }

    [Fact(DisplayName = "Given an instant already at the start of the month, when StartOfMonth runs, then the instant is returned unchanged")]
    public void StartOfMonthIsIdempotentAtMonthBoundary()
    {
        var instant = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

        ProxyBudgetMath.StartOfMonth(instant).ShouldBe(instant);
    }

    [Fact(DisplayName = "Given a mid-month instant, when SecondsUntilNextMonth runs, then the delta to the first of the next month is returned in seconds")]
    public void SecondsUntilNextMonthReturnsDeltaWithinMonth()
    {
        var instant = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
        var expected = (int)(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero) - instant).TotalSeconds;

        var seconds = ProxyBudgetMath.SecondsUntilNextMonth(instant);

        seconds.ShouldBe(expected);
        seconds.ShouldBe(12 * 60 * 60);
    }

    [Fact(DisplayName = "Given an instant in December, when SecondsUntilNextMonth runs, then the delta wraps to the first of January of the next year")]
    public void SecondsUntilNextMonthWrapsAcrossYearBoundary()
    {
        var instant = new DateTimeOffset(2026, 12, 15, 0, 0, 0, TimeSpan.Zero);
        var expected = (int)(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero) - instant).TotalSeconds;

        var seconds = ProxyBudgetMath.SecondsUntilNextMonth(instant);

        seconds.ShouldBe(expected);
        seconds.ShouldBe(17 * 24 * 60 * 60);
    }

    [Fact(DisplayName = "Given an instant late in the month, when SecondsUntilNextMonth runs, then the result equals StartOfNextMonth minus instant")]
    public void SecondsUntilNextMonthMatchesStartOfMonthOfNextMonth()
    {
        var instant = new DateTimeOffset(2026, 9, 30, 23, 59, 59, TimeSpan.Zero);

        var startOfNextMonth = ProxyBudgetMath.StartOfMonth(instant).AddMonths(1);
        var expected = (int)(startOfNextMonth - instant).TotalSeconds;

        var seconds = ProxyBudgetMath.SecondsUntilNextMonth(instant);

        seconds.ShouldBe(expected);
        seconds.ShouldBeGreaterThan(0);
    }

    [Fact(DisplayName = "Given whole-dollar amounts, when ToMicros runs, then each dollar becomes 1_000_000 micros (no fractional loss)")]
    public void ToMicrosConvertsWholeDollarsExactly()
    {
        ProxyBudgetMath.ToMicros(1m).ShouldBe(1_000_000L);
        ProxyBudgetMath.ToMicros(2.5m).ShouldBe(2_500_000L);
        ProxyBudgetMath.ToMicros(0m).ShouldBe(0L);
    }

    [Fact(DisplayName = "Given a fractional dollar amount, when ToMicros runs, then the result is rounded half-away-from-zero to the nearest micros")]
    public void ToMicrosRoundsFractionalAmounts()
    {
        ProxyBudgetMath.ToMicros(0.000001m).ShouldBe(1L);
        ProxyBudgetMath.ToMicros(0.0000015m).ShouldBe(2L);
    }

    [Fact(DisplayName = "Given a negative byte count or zero, when EstimateInputTokens runs, then zero is returned (no header means no cap)")]
    public void EstimateInputTokensReturnsZeroForZeroOrNegative()
    {
        ProxyBudgetMath.EstimateInputTokens(0).ShouldBe(0);
        ProxyBudgetMath.EstimateInputTokens(-1).ShouldBe(0);
        ProxyBudgetMath.EstimateInputTokens(long.MinValue).ShouldBe(0);
    }

    [Fact(DisplayName = "Given a positive byte count, when EstimateInputTokens runs, then the chars-per-4 heuristic is applied")]
    public void EstimateInputTokensAppliesCharsPerFourHeuristic()
    {
        ProxyBudgetMath.EstimateInputTokens(4).ShouldBe(1);
        ProxyBudgetMath.EstimateInputTokens(100).ShouldBe(25);
        ProxyBudgetMath.EstimateInputTokens(101).ShouldBe(25);
    }

    [Fact(DisplayName = "Given a byte count that would overflow int, when EstimateInputTokens runs, then the result is clamped to int.MaxValue")]
    public void EstimateInputTokensClampsAtIntMaxValue()
    {
        var bytes = int.MaxValue * 4L;

        var estimate = ProxyBudgetMath.EstimateInputTokens(bytes);

        estimate.ShouldBe(int.MaxValue);
    }
}
