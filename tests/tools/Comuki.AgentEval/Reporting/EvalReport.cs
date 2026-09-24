using Comuki.AgentTest.Runner.Reporting;
using Comuki.AgentTest.Runner.Reporting.Report;

namespace Comuki.AgentEval.Reporting;

/// <summary>
/// The WS10 envelope every eval run writes. Rendered to a JSON and
/// a matching markdown file by <see cref="EvalReportWriter"/> from
/// the same object (no drift between what the JSON says and what
/// the markdown says — same contract the runner's existing
/// <c>Reporting.ReportWriter</c> enforces).
/// </summary>
/// <param name="SchemaVersion">Envelope schema version; pinned to 1 today.</param>
/// <param name="Mode">Execution mode (fake | replay | live).</param>
/// <param name="CorpusDirectory">The corpus directory this run loaded entries from.</param>
/// <param name="StartedAt">UTC wall-clock start of the run.</param>
/// <param name="DurationMs">Total wall-clock duration in milliseconds.</param>
/// <param name="Summary">Pass/fail/skip counts (reuses the runner's existing <see cref="RunSummary"/> shape).</param>
/// <param name="AverageQualityScore">Average of <see cref="EvalEntryResult.Score"/>'s quality score across entries.</param>
/// <param name="Cost">Aggregate cost across every entry (zero in fake / replay modes).</param>
/// <param name="Entries">One <see cref="EvalEntryResult"/> per corpus entry.</param>
public sealed record EvalReport(
    int SchemaVersion = 1,
    string Mode = "fake",
    string CorpusDirectory = "",
    DateTimeOffset StartedAt = default,
    long DurationMs = 0,
    RunSummary? Summary = null,
    double AverageQualityScore = 0d,
    RunCost? Cost = null,
    IReadOnlyList<EvalEntryResult>? Entries = null)
{
    /// <summary>The <see cref="Summary"/> or a fresh empty one when unset.</summary>
    public RunSummary SummaryValue => Summary ?? new RunSummary();

    /// <summary>The <see cref="Cost"/> or a fresh empty one when unset.</summary>
    public RunCost CostValue => Cost ?? new RunCost();

    /// <summary>The <see cref="Entries"/> or an empty list when unset.</summary>
    public IReadOnlyList<EvalEntryResult> EntriesValue => Entries ?? [];
    /// <summary>
    /// Builds an <see cref="EvalReport"/> from a set of per-entry
    /// results. Mirrors <see cref="RunReport.FromResults"/>'s
    /// shape closely — same <see cref="RunSummary"/> reuse, same
    /// aggregate-cost summation, plus a corpus-specific
    /// <see cref="AverageQualityScore"/> summary field.
    /// </summary>
    public static EvalReport FromResults(
        string mode,
        string corpusDirectory,
        DateTimeOffset startedAt,
        TimeSpan duration,
        IReadOnlyList<EvalEntryResult> entries)
    {
        var passed = entries.Count(static entry => entry.Passed);
        var failed = entries.Count(static entry => !entry.Passed);
        var summary = new RunSummary
        {
            Total = entries.Count,
            Passed = passed,
            Failed = failed,
            Skipped = 0,
        };

        var cost = new RunCost
        {
            UsdMicros = entries.Sum(static entry => entry.Cost.UsdMicros),
            TokensIn = entries.Sum(static entry => entry.Cost.TokensIn),
            TokensOut = entries.Sum(static entry => entry.Cost.TokensOut),
        };

        var avg = entries.Count == 0
            ? 0d
            : entries.Average(static entry => entry.Score.QualityScore);

        return new EvalReport
        {
            Mode = mode,
            CorpusDirectory = corpusDirectory,
            StartedAt = startedAt,
            DurationMs = (long)duration.TotalMilliseconds,
            Summary = summary,
            AverageQualityScore = avg,
            Cost = cost,
            Entries = entries,
        };
    }
}
