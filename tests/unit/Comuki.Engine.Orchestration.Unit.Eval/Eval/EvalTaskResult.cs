namespace Comuki.Engine.Orchestration.Unit.Eval.Eval;

/// <summary>
/// A single point where the captured run diverged from the expected
/// outcome. The runner accumulates these per task; the writer renders
/// them in the markdown report under the failing task.
/// </summary>
/// <param name="Field">Which field differed: "transition-log" / "final-status" / "failure-message".</param>
/// <param name="Expected">The expected value (as a string).</param>
/// <param name="Actual">The captured value (as a string).</param>
public sealed record EvalMismatch(string Field, string Expected, string Actual);

/// <summary>
/// Captured outcome of running one <see cref="EvalTask"/> through the
/// runner. <see cref="Passed"/> is true when no mismatches were
/// collected (or, for negative tests, when the expected exception was
/// raised). The runner writes one of these per task into the
/// <see cref="EvalReport"/>.
/// </summary>
/// <param name="Task">The task that was run.</param>
/// <param name="Passed">Whether the outcome matched <see cref="EvalTask.Expected"/>.</param>
/// <param name="ActualTransitionLog">Statuses the aggregate actually passed through.</param>
/// <param name="Mismatches">Per-field mismatches; empty on success.</param>
/// <param name="DurationMs">Wall-clock duration of the run, in milliseconds.</param>
public sealed record EvalTaskResult(
    EvalTask Task,
    bool Passed,
    IReadOnlyList<string> ActualTransitionLog,
    IReadOnlyList<EvalMismatch> Mismatches,
    long DurationMs);

/// <summary>
/// Top-level report: one <see cref="EvalSuite"/> run against a set of
/// golden tasks. Aggregates pass/fail counts so the markdown writer can
/// surface the headline number.
/// </summary>
/// <param name="Suite">Logical suite name (e.g. <c>"engine.orchestration.status-machines"</c>).</param>
/// <param name="RunAt">When the suite was run.</param>
/// <param name="Results">One entry per task, in the order the runner saw them.</param>
public sealed record EvalReport(
    string Suite,
    DateTimeOffset RunAt,
    IReadOnlyList<EvalTaskResult> Results)
{
    /// <summary>Number of tasks that passed — convenience for the writer.</summary>
    public int PassedCount => Results.Count(static result => result.Passed);

    /// <summary>Number of tasks that failed — convenience for the writer.</summary>
    public int FailedCount => Results.Count(static result => !result.Passed);
}
