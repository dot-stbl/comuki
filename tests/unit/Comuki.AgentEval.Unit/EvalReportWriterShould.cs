using System.Text.Json;
using Comuki.AgentEval.Corpus;
using Comuki.AgentEval.Judges;
using Comuki.AgentEval.Reporting;
using Comuki.AgentEval.Scoring;
using Comuki.AgentTest.Runner.Reporting.Report;
using Comuki.AgentTest.Runner.Scenarios;
using Shouldly;
using Xunit;

namespace Comuki.AgentEval.Unit;

/// <summary>
/// Proves <see cref="EvalReportWriter.WriteAsync"/> writes a JSON+markdown
/// pair from the same <see cref="EvalReport"/> object (no drift between
/// the two), the one-line stdout verdict shape, and that per-entry
/// section rows in the markdown mention the scenario name and the
/// PASS/FAIL verdict.
/// </summary>
public sealed class EvalReportWriterShould : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"comuki-agent-eval-report-unit-{Guid.NewGuid():N}");

    [Fact(DisplayName = "Given an all-passing report, when written, then the verdict is PASS 2/2 and both files exist")]
    public async Task WriteBothFilesAndPassVerdictAsync()
    {
        var report = EvalReport.FromResults(
            "agent-eval",
            "fake",
            DateTimeOffset.UtcNow,
            TimeSpan.FromSeconds(1),
            [
                MakeEntry(scenarioName: "null-guard-simple", passed: true),
                MakeEntry(scenarioName: "refactor-magic-number", passed: true),
            ]);
        var basePath = Path.Combine(directory, "report");

        var verdict = await EvalReportWriter.WriteAsync(report, basePath, TestContext.Current.CancellationToken);

        verdict.ShouldBe("PASS 2/2");
        File.Exists(basePath + ".json").ShouldBeTrue();
        File.Exists(basePath + ".md").ShouldBeTrue();
    }

    [Fact(DisplayName = "Given a failing report, when written, then the verdict names the markdown path and the markdown names the failing scenario")]
    public async Task WriteFailVerdictWithFailingScenarioInMarkdownAsync()
    {
        var report = EvalReport.FromResults(
            "agent-eval",
            "fake",
            DateTimeOffset.UtcNow,
            TimeSpan.FromMilliseconds(200),
            [MakeEntry(scenarioName: "ambiguous-symptom-ticket", passed: false)]);
        var basePath = Path.Combine(directory, "fail-report");

        var verdict = await EvalReportWriter.WriteAsync(report, basePath, TestContext.Current.CancellationToken);

        verdict.ShouldStartWith("FAIL 1/1");
        var markdown = await File.ReadAllTextAsync(basePath + ".md", TestContext.Current.CancellationToken);
        markdown.ShouldContain("ambiguous-symptom-ticket");
        markdown.ShouldContain("FAIL");
    }

    [Fact(DisplayName = "Given a report, when written, then the JSON and markdown reflect the same summary counts (no drift)")]
    public async Task KeepJsonAndMarkdownInSyncAsync()
    {
        var report = EvalReport.FromResults(
            "agent-eval",
            "fake",
            DateTimeOffset.UtcNow,
            TimeSpan.FromSeconds(2),
            [
                MakeEntry(scenarioName: "null-guard-simple", passed: true),
                MakeEntry(scenarioName: "refactor-magic-number", passed: false),
            ]);
        var basePath = Path.Combine(directory, "sync-report");

        await EvalReportWriter.WriteAsync(report, basePath, TestContext.Current.CancellationToken);

        var json = await File.ReadAllTextAsync(basePath + ".json", TestContext.Current.CancellationToken);
        var markdown = await File.ReadAllTextAsync(basePath + ".md", TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(json);
        var summary = document.RootElement.GetProperty("summary");
        summary.GetProperty("total").GetInt32().ShouldBe(2);
        summary.GetProperty("passed").GetInt32().ShouldBe(1);
        summary.GetProperty("failed").GetInt32().ShouldBe(1);

        markdown.ShouldContain("null-guard-simple");
        markdown.ShouldContain("refactor-magic-number");
        markdown.ShouldContain("|2|1|1|");
        markdown.ShouldContain("avg quality");
    }

    [Fact(DisplayName = "Given a report with no entries, when written, then the markdown contains 'No entries' and the verdict is PASS 0/0")]
    public async Task WriteEmptyReportAsync()
    {
        var report = EvalReport.FromResults(
            "agent-eval",
            "fake",
            DateTimeOffset.UtcNow,
            TimeSpan.Zero,
            []);
        var basePath = Path.Combine(directory, "empty-report");

        var verdict = await EvalReportWriter.WriteAsync(report, basePath, TestContext.Current.CancellationToken);

        verdict.ShouldBe("PASS 0/0");
        var markdown = await File.ReadAllTextAsync(basePath + ".md", TestContext.Current.CancellationToken);
        markdown.ShouldContain("No entries");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static EvalEntryResult MakeEntry(string scenarioName, bool passed)
    {
        var score = new EvalScore(
            DeterministicPass: passed,
            Deterministic:
            [
                new DeterministicVerdict { JudgeName = "diff-applies", Passed = passed, Message = "fake verdict" },
            ],
            Judge: JudgeOutcome.Skipped("no live env"),
            QualityScore: passed ? 1d : 0d,
            Passed: passed);

        return new EvalEntryResult
        {
            ScenarioName = scenarioName,
            Difficulty = "easy",
            Passed = score.Passed,
            Score = score,
            Cost = new RunCost(),
            DurationMs = 100L,
            ArtifactPaths = ["/tmp/none"],
        };
    }
}
