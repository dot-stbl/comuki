namespace Comuki.Modules.Proxy.Application.Budgeting;

/// <summary>
/// Pure date-arithmetic helpers for the proxy budget enforcer — month
/// boundary + month length + USD → micros conversion. Pure functions
/// only; extracted so the enforcer stays free of private helpers (per
/// <c>class-layout-and-tooling.md</c> §1a). <c>internal</c> because the
/// enforcer consumes it from a sibling file in the same assembly.
/// </summary>
internal static class ProxyBudgetMath
{
    /// <summary>First instant of the calendar month that contains <paramref name="instant"/>.</summary>
    /// <param name="instant"></param>
    public static DateTimeOffset StartOfMonth(DateTimeOffset instant)
    {
        return new DateTimeOffset(instant.Year, instant.Month, 1, 0, 0, 0, instant.Offset);
    }

    /// <summary>
    /// Seconds until the next month boundary (capped at
    /// <see cref="int.MaxValue"/> to fit the <c>ProxyBudgetVerdict</c> contract).
    /// Zero when <paramref name="instant"/> is already a month boundary.
    /// </summary>
    /// <param name="instant"></param>
    public static int SecondsUntilNextMonth(DateTimeOffset instant)
    {
        var nextMonth = instant.Month == 12
            ? new DateTimeOffset(instant.Year + 1, 1, 1, 0, 0, 0, instant.Offset)
            : new DateTimeOffset(instant.Year, instant.Month + 1, 1, 0, 0, 0, instant.Offset);
        var delta = nextMonth - instant;
        return (int)Math.Min(int.MaxValue, Math.Max(0, delta.TotalSeconds));
    }

    /// <summary>USD → USD micros (1 USD = 1_000_000).</summary>
    /// <param name="usd"></param>
    public static long ToMicros(decimal usd)
    {
        var scaled = usd * 1_000_000m;
        return (long)decimal.Round(scaled, MidpointRounding.AwayFromZero);
    }
}
