using System.Text.Json;
using Comuki.AgentEval;
using Comuki.AgentEval.Corpus;
using Comuki.AgentEval.History;
using Comuki.AgentEval.Pi;
using Comuki.AgentTest.Runner.Execution.Budget;
using Comuki.AgentTest.Runner.Scenarios;
using Shouldly;
using Xunit;

namespace Comuki.AgentEval.Unit;

/// <summary>
/// The single E2E fake-mode fact in this project — calls
/// <see cref="EvalRun.RunAsync"/> in-process against
/// <c>tests/fixtures/scenarios/agent-eval</c>, gated by
/// <see cref="RealPiInstaller.InstallAsync"/> succeeding. Skips
/// gracefully (never FAILs) when bun/pi cannot be installed in the
/// sandbox — that's the documented acceptance behavior.
/// </summary>
/// <remarks>
/// This is the only fact in the whole project allowed to spawn a real
/// <c>bun</c>/<c>pi</c> process. Bounded overall by a hard ceiling via
/// <see cref="CancellationTokenSource"/> so a hung child can't wedge the
/// test run forever — sized generously above <see cref="EvalRun.PerEntryTimeout"/>'s
/// own 5-minute default, since this run walks every corpus entry
/// sequentially and each one cold-starts a real pi process even in
/// fake mode.
/// </remarks>
public sealed class AgentEvalCorpusShould
{
    [Fact(DisplayName = "Given a real pi install succeeds, when EvalRun runs against the agent-eval corpus in fake mode, then a report is written, history appends one line, and Total equals however many entries the corpus has")]
    public async Task EvalRunAgainstCorpusProducesReportAndAppendsHistoryAsync()
    {
        var piInstall = await RealPiInstaller.InstallAsync(TestContext.Current.CancellationToken);
        Assert.SkipUnless(
            piInstall.Succeeded,
            $"{piInstall.Reason} — this fact is the only one in the project allowed to spawn real pi; it skips when bun or the vendored pi tarball is unavailable in the sandbox.");

        var repoRoot = LocateRepoRoot();
        var corpusDirectory = Path.Combine(repoRoot, "tests", "fixtures", "scenarios", "agent-eval");
        var expectedEntryCount = CorpusLoader.LoadDirectory(corpusDirectory).Count;
        Assert.SkipUnless(
            expectedEntryCount > 0,
            $"corpus directory '{corpusDirectory}' contains no scenario files — fixture is empty; skipping E2E fact until the corpus lands.");

        // EvalRun always appends to <repoRoot>/artifacts/agent-eval/history.jsonl,
        // not to a caller-supplied path — so we read the line count from
        // there before and after, then delete the file in finally to leave
        // the workspace clean (the E2E fact is the only writer of this file).
        var historyPath = Path.Combine(repoRoot, "artifacts", "agent-eval", "history.jsonl");
        var historyBefore = File.Exists(historyPath)
            ? File.ReadAllLines(historyPath).Length
            : 0;

        try
        {
            // EvalRun.PerEntryTimeout defaults to 5 minutes and this run
            // walks 5 corpus entries sequentially (real pi cold-starts each
            // time even in fake mode) — a 2-minute outer ceiling was
            // tighter than even one entry's own internal budget and fired
            // before the run had a fair chance to finish. 10 minutes gives
            // real headroom above the worst well-behaved case while still
            // being a hard bound (never unbounded).
            using var ceiling = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            var reportBasePath = Path.Combine(
                Path.GetTempPath(),
                $"comuki-agent-eval-e2e-{Guid.NewGuid():N}-report");

            var options = new EvalRunOptions(
                CorpusDirectory: corpusDirectory,
                Mode: ScenarioModelMode.Fake,
                BudgetCap: BudgetCap.Unlimited,
                LiveUpstreamBaseUrl: null,
                LiveUpstreamToken: null,
                PiExecutablePath: piInstall.ExecutablePath,
                OutputBasePath: reportBasePath,
                JudgeClient: null);

            var report = await EvalRun.RunAsync(options, ceiling.Token);
            report.ShouldNotBeNull();

            report.SummaryValue.Total.ShouldBe(expectedEntryCount);

            File.Exists(reportBasePath + ".json").ShouldBeTrue();
            File.Exists(reportBasePath + ".md").ShouldBeTrue();

            var historyAfter = File.Exists(historyPath) ? File.ReadAllLines(historyPath).Length : 0;
            (historyAfter - historyBefore).ShouldBe(1);

            using var historyDoc = JsonDocument.Parse(File.ReadAllLines(historyPath)[^1]);
            historyDoc.RootElement.GetProperty("total").GetInt32().ShouldBe(expectedEntryCount);
            historyDoc.RootElement.GetProperty("mode").GetString().ShouldBe("fake");
        }
        finally
        {
            if (File.Exists(historyPath))
            {
                File.Delete(historyPath);
            }
        }
    }

    private static string LocateRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "comuki.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("could not locate the repo root (comuki.slnx) from " + AppContext.BaseDirectory);
    }
}
