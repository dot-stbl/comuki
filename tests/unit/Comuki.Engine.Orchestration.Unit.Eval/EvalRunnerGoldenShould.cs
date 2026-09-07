using Shouldly;
using Xunit;

namespace Comuki.Engine.Orchestration.Unit.Eval;

/// <summary>
/// End-to-end eval: load every JSON fixture under <c>Golden/</c>, parse
/// it through <see cref="EvalJsonTaskParser"/>, run it through
/// <see cref="EvalRunner"/> against the real engine orchestrator state
/// machines, and assert pass/fail. The runner is the regression detector
/// — if the production transition table changes in a way that breaks
/// the recorded expected outcomes, this test fails with a per-task
/// mismatch list.
/// </summary>
public sealed class EvalRunnerGoldenShould
{
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    private static IEnumerable<string> GoldenFiles()
    {
        var goldenDirectory = Path.Combine(AppContext.BaseDirectory, "Golden");
        Directory.Exists(goldenDirectory).ShouldBeTrue($"golden task directory should exist at {goldenDirectory}");

        return Directory.EnumerateFiles(goldenDirectory, "*.json").OrderBy(static path => path, StringComparer.Ordinal);
    }

    private static EvalTask Parse(string path)
    {
        try
        {
            return EvalJsonTaskParser.ParseFile(path);
        }
        catch (EvalParseException exception)
        {
            throw new Xunit.Sdk.XunitException(
                $"golden task at '{path}' failed to parse: {exception.Message} (origin={exception.Origin})");
        }
    }

    [Fact(DisplayName = "Given all golden tasks, when the runner walks them, then each task passes against the engine orchestrator state machines")]
    public void RunAllGoldenTasksAndPass()
    {
        var failingTasks = new List<string>();

        foreach (var path in GoldenFiles())
        {
            var task = Parse(path);
            var result = EvalRunner.Run(task, FixedNow);

            if (!result.Passed)
            {
                var diagnostics = string.Join(
                    "; ",
                    result.Mismatches.Select(static mismatch => $"{mismatch.Field} expected=`{mismatch.Expected}` actual=`{mismatch.Actual}`"));
                failingTasks.Add($"{task.Id}: {diagnostics}");
            }
        }

        failingTasks.ShouldBeEmpty(
            $"every golden task should pass; failing: {string.Join(" | ", failingTasks)}");
    }

    [Fact(DisplayName = "Given the golden suite, when the runner walks it, then every task is reported (no silent skips)")]
    public void CoverAtLeastThreeTasks()
    {
        var paths = GoldenFiles().ToArray();

        paths.Length.ShouldBeGreaterThanOrEqualTo(3, "the seed suite should cover at least three distinct paths");
    }

    [Fact(DisplayName = "Given the golden suite, when the runner builds a report, then the pass count matches the file count")]
    public void ReportCountsMatchTaskCount()
    {
        var paths = GoldenFiles().ToArray();
        var results = new List<EvalTaskResult>(paths.Length);

        foreach (var path in paths)
        {
            var task = Parse(path);
            results.Add(EvalRunner.Run(task, FixedNow));
        }

        var report = new EvalReport(
            Suite: "engine.orchestration.status-machines",
            RunAt: DateTimeOffset.UtcNow,
            Results: results);

        report.Results.Count.ShouldBe(paths.Length);
        report.PassedCount.ShouldBeGreaterThan(0);
        report.FailedCount.ShouldBe(0);
    }
}
