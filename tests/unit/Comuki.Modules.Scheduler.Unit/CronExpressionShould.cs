using Comuki.Modules.Scheduler.Domain.Scheduling;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Scheduler.Unit;

/// <summary>
/// <see cref="CronExpression"/> parser + next-fire search. Covers the 5
/// field shapes (<c>*</c>, <c>n</c>, <c>n-m</c>, <c>*/k</c>, comma list),
/// month and day-of-week name aliases, the strict "next minute after
/// anchor" lower bound, the 4-year search cap (returns null when the
/// expression never fires inside the window), and the
/// <see cref="FormatException"/> contract on malformed input.
/// </summary>
public sealed class CronExpressionShould
{
    [Fact(DisplayName = "Given \"* * * * *\", when NextFireAfter is called, then the result is the next minute strictly after the anchor")]
    public void EveryMinuteReturnsStrictlyNextMinute()
    {
        var cron = CronExpression.Parse("* * * * *");
        var anchor = new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

        var next = cron.NextFireAfter(anchor);

        next.ShouldBe(new DateTimeOffset(2026, 9, 6, 12, 1, 0, TimeSpan.Zero));
    }

    [Fact(DisplayName = "Given \"0 9 * * *\" before 09:00, when NextFireAfter is called, then the result is 09:00 same day")]
    public void DailyNineAmReturnsSameDayWhenBeforeNine()
    {
        var cron = CronExpression.Parse("0 9 * * *");
        var anchor = new DateTimeOffset(2026, 9, 6, 8, 30, 0, TimeSpan.Zero);

        var next = cron.NextFireAfter(anchor);

        next.ShouldBe(new DateTimeOffset(2026, 9, 6, 9, 0, 0, TimeSpan.Zero));
    }

    [Fact(DisplayName = "Given \"0 9 * * *\" at or after 09:00, when NextFireAfter is called, then the result is 09:00 the next day")]
    public void DailyNineAmReturnsNextDayWhenAtOrAfterNine()
    {
        var cron = CronExpression.Parse("0 9 * * *");
        var anchor = new DateTimeOffset(2026, 9, 6, 9, 0, 0, TimeSpan.Zero);

        var next = cron.NextFireAfter(anchor);

        next.ShouldBe(new DateTimeOffset(2026, 9, 7, 9, 0, 0, TimeSpan.Zero));
    }

    [Fact(DisplayName = "Given \"*/15 * * * *\" mid-quarter, when NextFireAfter is called, then the result is the next minute divisible by 15")]
    public void EveryFifteenMinutesAlignsToNextQuarter()
    {
        var cron = CronExpression.Parse("*/15 * * * *");
        var anchor = new DateTimeOffset(2026, 9, 6, 12, 7, 0, TimeSpan.Zero);

        var next = cron.NextFireAfter(anchor);

        next.ShouldBe(new DateTimeOffset(2026, 9, 6, 12, 15, 0, TimeSpan.Zero));
    }

    [Fact(DisplayName = "Given \"0 0 1 JAN *\" mid-year, when NextFireAfter is called, then the result is the next 1 January 00:00")]
    public void YearlyJanuaryFirstReturnsNextJanFirst()
    {
        var cron = CronExpression.Parse("0 0 1 JAN *");
        var anchor = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

        var next = cron.NextFireAfter(anchor);

        next.ShouldBe(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact(DisplayName = "Given \"* 9-17 * * MON-FRI\" on a Saturday, when NextFireAfter is called, then the result is the following Monday at 09:00")]
    public void WeekdayBusinessHoursFromSaturdayReturnsMondayMorning()
    {
        var cron = CronExpression.Parse("* 9-17 * * MON-FRI");
        var anchor = new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

        var next = cron.NextFireAfter(anchor).GetValueOrDefault();

        next.ShouldBe(new DateTimeOffset(2026, 9, 7, 9, 0, 0, TimeSpan.Zero));
        next.DayOfWeek.ShouldBe(DayOfWeek.Monday);
    }

    [Fact(DisplayName = "Given an expression that never fires inside the 4-year window, when NextFireAfter is called, then it returns null")]
    public void ImpossibleDateExhaustsSearchAndReturnsNull()
    {
        // Feb 31 is impossible — Feb has at most 29 days, so the search
        // walks every minute in the 4-year window without ever matching.
        var cron = CronExpression.Parse("0 0 31 2 *");
        var anchor = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var next = cron.NextFireAfter(anchor);

        next.ShouldBeNull();
    }

    [Theory(DisplayName = "Given a malformed expression, when Parse is called, then it throws FormatException")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("* * * *")]
    [InlineData("* * * * * *")]
    [InlineData("*/-1 * * * *")]
    [InlineData("60 * * * *")]
    [InlineData("-1 * * * *")]
    [InlineData("0 24 * * *")]
    [InlineData("0 0 32 * *")]
    [InlineData("0 0 1 13 *")]
    [InlineData("0 0 1 FOO *")]
    [InlineData("0 0 * * BAR")]
    public void RejectMalformedExpression(string expression)
    {
        Should.Throw<FormatException>(() => CronExpression.Parse(expression));
    }

    [Fact(DisplayName = "Given \"0 0 1 * MON\" at 2026-09-01, when NextFireAfter is called, then the result is 2027-02-01 00:00 (Cronos DOM AND DOW — the next 1st of a month that is also a Monday)")]
    public void DomAndDowSemanticsResolvesToFirstMatch()
    {
        // Cronos follows the de-facto standard that day-of-month
        // AND day-of-week are both required when both are restricted
        // (this matches the hand-rolled parser's behavior, not
        // the Vixie-cron DOM OR DOW variant the brief speculated
        // about). 2026-09-01 is a Tuesday, so the expression waits
        // until the next 1st of a month that is also a Monday —
        // 2027-02-01 is the next such date.
        var cron = CronExpression.Parse("0 0 1 * MON");
        var anchor = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

        var next = cron.NextFireAfter(anchor);

        next.ShouldBe(new DateTimeOffset(2027, 2, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact(DisplayName = "Given \"0 0 29 2 *\" at 2024-01-01, when NextFireAfter is called, then the result is 2024-02-29 00:00 (Cronos leap-year handling)")]
    public void LeapDayFeb29FiresOnNextLeapYear()
    {
        // 2024 is a leap year, so the first Feb 29 after the
        // anchor is 2024-02-29 itself. Cronos's own leap-year
        // handling matches the hand-rolled parser's answer here.
        var cron = CronExpression.Parse("0 0 29 2 *");
        var anchor = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var next = cron.NextFireAfter(anchor);

        next.ShouldBe(new DateTimeOffset(2024, 2, 29, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact(DisplayName = "Given \"0 0 29 2 *\" at 2024-03-01, when NextFireAfter is called, then the result is 2028-02-29 00:00 (next leap year, 4 years later)")]
    public void LeapDayFeb29SkipsNonLeapYears()
    {
        // After 2024-02-29, the next Feb 29 is 2028 (2025/2026/2027
        // are not leap years). The 4-year search cap would have
        // missed 2028 if the anchor was 2025; Cronos's own
        // forward search handles it without the cap. 2024-03-01 +
        // 4 years is 2028-03-01, just past the 4-year ceiling —
        // but the wrapper's ceiling includes the to-endpoint
        // (toInclusive=true), so 2028-02-29 is still in range.
        var cron = CronExpression.Parse("0 0 29 2 *");
        var anchor = new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero);

        var next = cron.NextFireAfter(anchor);

        next.ShouldBe(new DateTimeOffset(2028, 2, 29, 0, 0, 0, TimeSpan.Zero));
    }
}
