using Comuki.AgentTest.Runner.Reporting.Report;

namespace Comuki.AgentTest.Runner.Reporting;

/// <summary>
/// The report envelope every tier's runner writes (design.md "Report
/// format for agents") — one object, rendered as both
/// <c>&lt;path&gt;.json</c> and <c>&lt;path&gt;.md</c> by
/// <see cref="ReportWriter"/> so the two never drift apart.
/// </summary>
public sealed record RunReport
{
    /// <summary>Envelope schema version.</summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>Test tier this report covers — <c>agent-loop</c> for T2a/T2b.</summary>
    public string Tier { get; init; } = "agent-loop";

    /// <summary>Execution mode of the scenarios in this report — <c>fake</c>/<c>replay</c>/<c>live</c>.</summary>
    public string Mode { get; init; } = "fake";

    /// <summary>UTC wall-clock start of the run.</summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>Total wall-clock duration in milliseconds.</summary>
    public long DurationMs { get; init; }

    /// <summary>Pass/fail/skip counts.</summary>
    public RunSummary Summary { get; init; } = new();

    /// <summary>One entry per failed scenario, naming the failing assertion — never a raw stack trace.</summary>
    public IReadOnlyList<RunFailure> Failures { get; init; } = [];

    /// <summary>Aggregate cost across every scenario in this report (zero in fake mode).</summary>
    public RunCost Cost { get; init; } = new();

    /// <summary>Builds the report from a set of per-scenario results.</summary>
    /// <param name="tier">Tier name (e.g. <c>agent-loop</c>).</param>
    /// <param name="mode">Execution mode (e.g. <c>fake</c>).</param>
    /// <param name="startedAt">UTC start timestamp.</param>
    /// <param name="duration">Total wall-clock duration.</param>
    /// <param name="results">One result per scenario run.</param>
    public static RunReport FromResults(
        string tier,
        string mode,
        DateTimeOffset startedAt,
        TimeSpan duration,
        IReadOnlyList<ScenarioResult> results)
    {
        return new RunReport
        {
            Tier = tier,
            Mode = mode,
            StartedAt = startedAt,
            DurationMs = (long)duration.TotalMilliseconds,
            Summary = new RunSummary
            {
                Total = results.Count,
                Passed = results.Count(static result => result.Passed),
                Failed = results.Count(static result => !result.Passed && !result.Skipped),
                Skipped = results.Count(static result => result.Skipped),
            },
            Failures = [.. results
                .Where(static result => !result.Passed && !result.Skipped)
                .Select(static result => new RunFailure
                {
                    Scenario = result.ScenarioName,
                    Stage = result.FailedStage ?? "unknown",
                    Message = result.FailureMessage ?? "no failure message recorded",
                    ArtifactPaths = result.ArtifactPaths,
                })],
            Cost = new RunCost
            {
                UsdMicros = results.Sum(static result => result.Cost.UsdMicros),
                TokensIn = results.Sum(static result => result.Cost.TokensIn),
                TokensOut = results.Sum(static result => result.Cost.TokensOut),
            },
        };
    }
}
