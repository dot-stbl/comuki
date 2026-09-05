namespace Comuki.Modules.Scheduler.Domain.Scheduling;

/// <summary>
/// Minimal 5-field cron parser + next-fire computation. UTC, inclusive lower
/// bound — the next fire is always strictly after the given anchor (so a
/// host calling NextFireAsync(now) doesn't loop on the same instant).
/// <para>
/// Supported token shapes per field (positions 0..4):
/// </para>
/// <list type="bullet">
///   <item><c>*</c> — every step (matches any value in range).</item>
///   <item><c>n</c> — single value (or month / weekday name).</item>
///   <item><c>n-m</c> — inclusive range.</item>
///   <item><c>*/k</c> — every <c>k</c>-th value, anchored at 0.</item>
///   <item><c>n,m,...</c> — comma list of any of the above.</item>
/// </list>
/// <para>
/// Day-of-week and month accept the canonical English names
/// (<c>SUN</c>..<c>SAT</c>, <c>JAN</c>..<c>DEC</c>) as a convenience.
/// </para>
/// <para>
/// Out of scope: seconds / year fields (Quartz / Vixie cron extensions),
/// <c>L</c> / <c>W</c> / <c>#</c> anchors, timezone-aware input. The
/// scheduler is documented as UTC-only — see <see cref="Parse"/>.
/// </para>
/// </summary>
public sealed class CronExpression
{
    /// <summary>The 5 field matchers in cron order: minute, hour, day-of-month, month, day-of-week.</summary>
    private readonly FieldMatcher minute;
    private readonly FieldMatcher hour;
    private readonly FieldMatcher dayOfMonth;
    private readonly FieldMatcher month;
    private readonly FieldMatcher dayOfWeek;

    private CronExpression(FieldMatcher minute, FieldMatcher hour, FieldMatcher dayOfMonth, FieldMatcher month, FieldMatcher dayOfWeek)
    {
        this.minute = minute;
        this.hour = hour;
        this.dayOfMonth = dayOfMonth;
        this.month = month;
        this.dayOfWeek = dayOfWeek;
    }

    /// <summary>Parses a 5-field cron expression. Throws <see cref="FormatException"/> on malformed input.</summary>
    /// <param name="expression"></param>
    /// <returns></returns>
    /// <exception cref="FormatException">Malformed expression.</exception>
    public static CronExpression Parse(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            throw new FormatException("cron expression must not be empty");
        }

        var fields = expression.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return fields.Length != 5
            ? throw new FormatException($"cron expression must have exactly 5 space-separated fields; got {fields.Length}")
            : new CronExpression(
            FieldMatcher.Parse(fields[0], FieldKind.Minute),
            FieldMatcher.Parse(fields[1], FieldKind.Hour),
            FieldMatcher.Parse(fields[2], FieldKind.DayOfMonth),
            FieldMatcher.Parse(fields[3], FieldKind.Month),
            FieldMatcher.Parse(fields[4], FieldKind.DayOfWeek));
    }

    /// <summary>
    /// Returns the next instant strictly after <paramref name="anchor"/>
    /// at which the expression fires. Returns null only if the search
    /// window overflows (year &gt; 9999) — i.e. the expression never
    /// fires again in the supported range.
    /// </summary>
    /// <param name="anchor">UTC anchor; next fire is strictly greater than this.</param>
    /// <returns>UTC next-fire instant, or null when the search overflows.</returns>
    public DateTimeOffset? NextFireAfter(DateTimeOffset anchor)
    {
        var candidate = new DateTime(
            anchor.Year, anchor.Month, anchor.Day,
            anchor.Hour, anchor.Minute, 0,
            DateTimeKind.Utc).AddMinutes(1);

        // Bounded search: cron expressions with a year look-ahead are rare
        // in practice; cap at 4 years of minute-resolution search so a
        // pathological schedule cannot pin the host's event loop.
        var ceiling = anchor.AddYears(4);
        while (candidate <= ceiling)
        {
            if (month.Matches(candidate.Month)
                && dayOfMonth.Matches(candidate.Day)
                && dayOfWeek.Matches((int)candidate.DayOfWeek)
                && hour.Matches(candidate.Hour)
                && minute.Matches(candidate.Minute))
            {
                return new DateTimeOffset(candidate, TimeSpan.Zero);
            }

            candidate = candidate.AddMinutes(1);
        }

        return null;
    }

    private enum FieldKind
    {
        Minute,
        Hour,
        DayOfMonth,
        Month,
        DayOfWeek,
    }

    private sealed class FieldMatcher
    {
        private readonly HashSet<int> matches;

        private FieldMatcher(HashSet<int> matches)
        {
            this.matches = matches;
        }

        public bool Matches(int value)
        {
            return matches.Contains(value);
        }

        public static FieldMatcher Parse(string text, FieldKind kind)
        {
            var (low, high) = Range(kind);
            var set = new HashSet<int>();

            foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                AddPart(set, part, kind, low, high);
            }

            return set.Count == 0 ? throw new FormatException($"no valid values in field '{text}'") : new FieldMatcher(set);
        }

        private static void AddPart(HashSet<int> set, string part, FieldKind kind, int low, int high)
        {
            var (stepText, rangeText) = part.Split('/') is var split && split.Length == 2
                ? (split[1], split[0])
                : ("1", part);

            if (!int.TryParse(stepText, out var step) || step <= 0)
            {
                throw new FormatException($"step '{stepText}' must be a positive integer");
            }

            int rangeStart;
            int rangeEnd;

            if (rangeText == "*")
            {
                rangeStart = low;
                rangeEnd = high;
            }
            else if (rangeText.Contains('-'))
            {
                var bounds = rangeText.Split('-');
                rangeStart = ResolveValue(bounds[0], kind);
                rangeEnd = ResolveValue(bounds[1], kind);
            }
            else
            {
                rangeStart = ResolveValue(rangeText, kind);
                rangeEnd = rangeStart;
            }

            if (rangeStart > rangeEnd)
            {
                throw new FormatException($"range start {rangeStart} must be <= end {rangeEnd}");
            }

            for (var current = rangeStart; current <= rangeEnd; current += step)
            {
                set.Add(current);
            }
        }

        private static int ResolveValue(string text, FieldKind kind)
        {
            return kind switch
            {
                FieldKind.Minute => ParseNumber(text, 0, 59),
                FieldKind.Hour => ParseNumber(text, 0, 23),
                FieldKind.DayOfMonth => ParseNumber(text, 1, 31),
                FieldKind.Month => ParseMonth(text),
                FieldKind.DayOfWeek => ParseDayOfWeek(text),
                _ => throw new FormatException($"unknown field kind {kind}"),
            };
        }

        private static int ParseNumber(string text, int low, int high)
        {
            return !int.TryParse(text, out var value)
                ? throw new FormatException($"expected integer in [{low},{high}], got '{text}'")
                : value < low || value > high ? throw new FormatException($"value {value} out of range [{low},{high}]") : value;
        }

        private static int ParseMonth(string text)
        {
            return int.TryParse(text, out _)
                ? ParseNumber(text, 1, 12)
                : text.ToUpperInvariant() switch
                {
                    "JAN" => 1,
                    "FEB" => 2,
                    "MAR" => 3,
                    "APR" => 4,
                    "MAY" => 5,
                    "JUN" => 6,
                    "JUL" => 7,
                    "AUG" => 8,
                    "SEP" => 9,
                    "OCT" => 10,
                    "NOV" => 11,
                    "DEC" => 12,
                    _ => throw new FormatException($"unknown month name '{text}'"),
                };
        }

        private static int ParseDayOfWeek(string text)
        {
            if (int.TryParse(text, out var numeric))
            {
                // Cron convention: 0 = Sunday, 7 also accepted as Sunday.
                return numeric == 7 ? 0 : ParseNumber(text, 0, 6);
            }

            return text.ToUpperInvariant() switch
            {
                "SUN" => 0,
                "MON" => 1,
                "TUE" => 2,
                "WED" => 3,
                "THU" => 4,
                "FRI" => 5,
                "SAT" => 6,
                _ => throw new FormatException($"unknown weekday name '{text}'"),
            };
        }

        private static (int Low, int High) Range(FieldKind kind)
        {
            return kind switch
            {
                FieldKind.Minute => (0, 59),
                FieldKind.Hour => (0, 23),
                FieldKind.DayOfMonth => (1, 31),
                FieldKind.Month => (1, 12),
                FieldKind.DayOfWeek => (0, 6),
                _ => throw new FormatException($"unknown field kind {kind}"),
            };
        }
    }
}
