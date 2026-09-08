namespace Comuki.Modules.Proxy.Application.Budgeting;

/// <summary>
/// Pure date-arithmetic helpers for the proxy budget enforcer — month
/// boundary + month length + USD → micros conversion, plus the per-call
/// input/output token estimation used by the request guard. Pure functions
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

    /// <summary>
    /// Token heuristic: <c>chars / 4</c>. Approximates the
    /// OpenAI/Anthropic "≈ 1 token per 4 chars" rule without forcing the
    /// proxy to buffer the request body just for a size check. The
    /// <paramref name="requestByteCount"/> is the
    /// <c>HttpContext.Request.ContentLength</c> — the transformer passes
    /// the header value, not the body. Returns 0 when the length is
    /// unknown so the caller can treat "no header" as "no cap".
    /// </summary>
    /// <param name="requestByteCount">Content-Length header in bytes.</param>
    public static int EstimateInputTokens(long requestByteCount)
    {
        return requestByteCount <= 0 ? 0 : (int)Math.Min(int.MaxValue, requestByteCount / 4);
    }
}
