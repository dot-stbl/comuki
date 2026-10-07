using Cronos;

namespace Comuki.Modules.Scheduler.Domain.Scheduling;

/// <summary>
/// 5-field cron parser + next-fire computation. UTC, inclusive lower
/// bound — the next fire is always strictly after the given anchor (so a
/// host calling <see cref="NextFireAfter"/> doesn't loop on the same
/// instant).
/// <para>
/// Backed by <see cref="Cronos.CronExpression"/> (add-scheduled-jobs
/// design.md "Cronos preferred; do not hand-roll"). The wrapper preserves
/// the original public API (<c>Parse</c> + <c>NextFireAfter</c>) so
/// <c>ScheduledJob</c> doesn't need to change.
/// </para>
/// <para>
/// <b>Parser delta vs the previous hand-rolled implementation:</b>
/// Cronos accepts expressions the previous parser rejected — most
/// notably reversed ranges like <c>0 9-8 * * *</c> (it accepts them
/// as if they wrapped modulo the field's range; the hand-rolled
/// parser threw <see cref="FormatException"/>). The semantics
/// <i>of expressions the original parser accepted</i> are unchanged:
/// the hand-rolled parser used DOM AND DOW (both required when both
/// restricted), and so does Cronos. The two cron fields the
/// scheduler actually uses today (<c>*/5 * * * *</c> and
/// <c>0 9 * * *</c>) are unaffected. Any saved cron string in the
/// database whose <i>previous behaviour</i> was "rejected at
/// parse-time" will now parse to a different firing time on the
/// next deploy — this is recorded in
/// <c>openspec/changes/add-scheduled-jobs/tasks.md</c> as a deploy
/// note so the operator can audit the change.
/// </para>
/// <para>
/// Supported token shapes per field (positions 0..4): the standard
/// cron shapes delegated to <see cref="Cronos.CronExpression.Parse(string)"/>:
/// <c>*</c>, <c>n</c>, <c>n-m</c>, <c>*/k</c>, comma list of any of the
/// above, month and day-of-week name aliases (<c>JAN</c>..<c>DEC</c>,
/// <c>SUN</c>..<c>SAT</c>). Leap-year Feb-29 (<c>0 0 29 2 *</c>) is
/// handled by Cronos and returns the next leap-year occurrence
/// (4 years, 8 years, ...) — verified by the test
/// <c>LeapDayFeb29FiresOnNextLeapYear</c>.
/// </para>
/// <para>
/// Out of scope: seconds / year fields (Quartz / Vixie cron extensions),
/// <c>L</c> / <c>W</c> / <c>#</c> anchors. The scheduler is documented
/// as UTC-only — see <see cref="Parse"/>.
/// </para>
/// </summary>
public sealed class CronExpression
{
    private readonly Cronos.CronExpression cron;

    private CronExpression(Cronos.CronExpression cron)
    {
        this.cron = cron;
    }

    /// <summary>Parses a 5-field cron expression. Throws <see cref="FormatException"/> on malformed input.</summary>
    /// <param name="expression"></param>
    /// <returns></returns>
    /// <exception cref="FormatException">Malformed expression (wrong field count, out-of-range value, unparseable alias).</exception>
    public static CronExpression Parse(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            throw new FormatException("cron expression must not be empty");
        }

        // Cronos's Parse(string) accepts 5 OR 6 fields. The original
        // contract here was 5 fields exactly, so preserve that:
        // 6-field input is a caller bug, surface it as FormatException
        // to keep ScheduledJobService's wrap-as-InvalidCronExpression
        // path the only failure mode callers see.
        var fields = expression.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 5)
        {
            throw new FormatException($"cron expression must have exactly 5 space-separated fields; got {fields.Length}");
        }

        try
        {
            return new CronExpression(Cronos.CronExpression.Parse(expression));
        }
        catch (CronFormatException ex)
        {
            throw new FormatException($"invalid cron expression '{expression}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Returns the next instant strictly after <paramref name="anchor"/>
    /// at which the expression fires. Returns null only if the search
    /// overflows the 4-year window — i.e. the expression never fires
    /// again in the supported range.
    /// </summary>
    /// <param name="anchor">UTC anchor; next fire is strictly greater than this.</param>
    /// <returns>UTC next-fire instant, or null when the search overflows.</returns>
    public DateTimeOffset? NextFireAfter(DateTimeOffset anchor)
    {
        // Bounded search: cron expressions with a year look-ahead are rare
        // in practice; cap at 4 years of minute-resolution search so a
        // pathological schedule (e.g. 0 0 31 2 * — Feb 31, never fires)
        // cannot pin the host's event loop. Cronos's GetOccurrences is
        // a forward-only generator over [from, to] (fromInclusive=false
        // matches the strictly-after contract), so the first yield
        // IS the next fire.
        var ceiling = anchor.AddYears(4);
        foreach (var occurrence in cron.GetOccurrences(
                     anchor.UtcDateTime,
                     ceiling.UtcDateTime,
                     fromInclusive: false,
                     toInclusive: true))
        {
            return new DateTimeOffset(occurrence, TimeSpan.Zero);
        }

        return null;
    }
}
