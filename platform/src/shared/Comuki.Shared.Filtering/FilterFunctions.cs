// Ported from Hybrid.Sdk.Shared.Filtering (console.x.sdk) — fidelity over house style.
using System.Globalization;

namespace Comuki.Shared.Filtering;

/// <summary>
///     Server-side functions evaluable as filter values. Each function takes a
///     well-typed argument parsed by the lexer and returns a concrete
///     <see cref="DateTimeOffset" /> that the comparison operator applies to the
///     field. Time is anchored to <see cref="DateTimeOffset.UtcNow" /> — never to
///     client-supplied timestamps, so a query like <c>createdAt&gt;=now(-7d)</c>
///     returns consistent results regardless of where the request originated.
/// </summary>
/// <remarks>
///     <para>
///         Only <c>now</c> is exposed in this revision. The set is intentionally
///         minimal — every new function is a new code path that needs a security
///         review and adversarial tests. Adding <c>today()</c>,
///         <c>startOfWeek()</c>, etc. is deferred until a real filter demands it.
///     </para>
///     <para>
///         Grammar (EBNF):
///         <code>
/// function       := 'now' '(' signedDuration ')'
/// signedDuration := ['-']? digits unit
/// unit           := 's' | 'm' | 'h' | 'd' | 'w'
///         </code>
///     </para>
/// </remarks>
public static class FilterFunctions
{
    /// <summary>
    ///     Evaluates <c>now(offset)</c> against <see cref="DateTimeOffset.UtcNow" />.
    ///     The <paramref name="argument" /> is the duration string captured by the
    ///     lexer (e.g. <c>-7d</c>, <c>1h</c>, <c>0d</c>); it is parsed and applied
    ///     to the current UTC timestamp. Returns the absolute point in time the
    ///     comparison operator should compare the field against.
    /// </summary>
    /// <param name="functionName">
    ///     Function identifier from the DSL, case-insensitive. Only <c>now</c> is
    ///     accepted — any other name throws <see cref="FilterParseException" />.
    /// </param>
    /// <param name="argument">
    ///     Raw argument text as captured by the lexer (e.g. <c>-7d</c>). Must be
    ///     a signed integer followed by a single-char unit.
    /// </param>
    /// <param name="position">
    ///     Source position for error messages.
    /// </param>
    /// <returns>
    ///     <c>DateTimeOffset.UtcNow + parsed offset</c>.
    /// </returns>
    /// <exception cref="FilterParseException">
    ///     Thrown for unknown function names, malformed durations, unknown units,
    ///     or out-of-range integer values.
    /// </exception>
    public static DateTimeOffset EvaluateNow(string functionName, string argument, int position)
    {
        if (!string.Equals(functionName, "now", StringComparison.OrdinalIgnoreCase))
        {
            throw new FilterParseException(
                $"Unknown function '{functionName}' (only 'now' is supported)",
                position);
        }

        var offset = ParseDuration(argument, position);
        return DateTimeOffset.UtcNow + offset;
    }

    /// <summary>
    ///     Parses a signed duration literal of the shape <c>[-]N[unit]</c>:
    ///     optional leading minus, mandatory integer, mandatory single-char unit.
    ///     The leading sign is restricted to <c>-</c> — <c>+7d</c> is rejected to
    ///     match the documented grammar (positive offsets are simply <c>7d</c>).
    /// </summary>
    /// <param name="text">Raw argument text from the lexer (no surrounding parens).</param>
    /// <param name="position">Source position for error messages.</param>
    /// <exception cref="FilterParseException">Malformed duration.</exception>
    public static TimeSpan ParseDuration(string text, int position)
    {
        if (string.IsNullOrEmpty(text) || text.Length < 2)
        {
            throw new FilterParseException(
                $"Invalid duration '{text}' (expected: [-]N[unit], e.g. -7d, 1h, 30m)",
                position);
        }

        var negative = false;
        var numberStart = 0;

        if (text[0] == '-')
        {
            negative = true;
            numberStart = 1;
        }
        else if (text[0] == '+')
        {
            // Reject explicit '+' even though int.TryParse would accept it: the
            // documented grammar is '[-]N[unit]', positive offsets are bare.
            throw new FilterParseException(
                $"Invalid duration '{text}' (use bare positive, e.g. '7d', not '+7d')",
                position);
        }
        else if (!char.IsDigit(text[0]))
        {
            throw new FilterParseException(
                $"Invalid duration '{text}' (expected integer at start)",
                position);
        }

        var unitIndex = text.Length - 1;

        // The unit must be a single char at the end; number occupies everything
        // between the optional sign and the unit. If numberStart > unitIndex, the
        // input is just "-" or empty — rejected below.
        if (numberStart > unitIndex)
        {
            throw new FilterParseException(
                $"Invalid duration '{text}' (missing number before unit)",
                position);
        }

        var numberPart = text[numberStart..unitIndex];
        var unit = text[unitIndex];

        if (!int.TryParse(numberPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            throw new FilterParseException(
                $"Invalid duration number '{numberPart}' in '{text}' (integer required)",
                position);
        }

        if (negative)
        {
            // Guard against int.MinValue negation overflow. The largest magnitude
            // we realistically need is a few years in either direction — reject
            // values that would push the resulting TimeSpan outside DateTimeOffset
            // range before applying the unit multiplier.
            if (value == int.MinValue)
            {
                throw new FilterParseException(
                    $"Duration '{text}' is out of range",
                    position);
            }

            value = -value;
        }

        var offset = unit switch
        {
            's' => TimeSpan.FromSeconds(value),
            'm' => TimeSpan.FromMinutes(value),
            'h' => TimeSpan.FromHours(value),
            'd' => TimeSpan.FromDays(value),
            'w' => TimeSpan.FromDays(value * 7),
            _ => throw new FilterParseException(
                $"Unknown duration unit '{unit}' in '{text}' (allowed: s, m, h, d, w)",
                position)
        };

        // Defensive range check: DateTimeOffset +/- TimeSpan must stay representable.
        // UtcNow +/- ~10000 days is far outside any realistic filter; reject anything
        // that would throw at evaluation time so the client sees 400, not 500.
        try
        {
            OverflowProbe(offset);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new FilterParseException(
                $"Duration '{text}' is out of representable range",
                position,
                ex);
        }

        return offset;
    }

    // The only reason this helper exists: surface the same
    // ArgumentOutOfRangeException that DateTimeOffset.UtcNow + offset raises
    // (the wrapper exception is FilterParseException at the call site).
    // The + operator has no TryAdd equivalent on DateTimeOffset, so this
    // probes by computing the offset. Result discarded by design — only the
    // throw matters.
    private static void OverflowProbe(TimeSpan offset)
    {
        _ = DateTimeOffset.UtcNow + offset;
    }
}
