using Comuki.AgentTest.Runner.Reporting;
using Shouldly;
using Xunit;

namespace Comuki.AgentTest.Runner.Unit;

/// <summary>
/// Proves <see cref="ReportWriter"/> writes the JSON+markdown pair from
/// the same <see cref="RunReport"/> object (design.md: "no drift between
/// what the JSON says and what the markdown says") and the one-line
/// stdout verdict shape.
/// </summary>
public sealed class ReportWriterShould : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"comuki-agenttest-report-unit-{Guid.NewGuid():N}");

    [Fact(DisplayName = "Given an all-passing report, when written, then the verdict is PASS n/n and both files exist")]
    public async Task WriteBothFilesAndPassVerdictAsync()
    {
        var report = RunReport.FromResults(
            "agent-loop",
            "fake",
            DateTimeOffset.UtcNow,
            TimeSpan.FromSeconds(1),
            [ScenarioResult.Success("add-null-check", TimeSpan.FromSeconds(1), [])]);
        var basePath = Path.Combine(directory, "report");

        var verdict = await ReportWriter.WriteAsync(report, basePath, TestContext.Current.CancellationToken);

        verdict.ShouldBe("PASS 1/1");
        File.Exists(basePath + ".json").ShouldBeTrue();
        File.Exists(basePath + ".md").ShouldBeTrue();
    }

    [Fact(DisplayName = "Given a failing report, when written, then the verdict names the markdown path and the markdown names the failing scenario")]
    public async Task WriteFailVerdictWithFailingScenarioInMarkdownAsync()
    {
        var report = RunReport.FromResults(
            "agent-loop",
            "fake",
            DateTimeOffset.UtcNow,
            TimeSpan.FromMilliseconds(200),
            [ScenarioResult.Failure("bad-image-label", "compute.start", "no such image: comuki-agent-test-worker:ws6-nonexistent-tag", TimeSpan.FromMilliseconds(200), [])]);
        var basePath = Path.Combine(directory, "fail-report");

        var verdict = await ReportWriter.WriteAsync(report, basePath, TestContext.Current.CancellationToken);

        verdict.ShouldBe($"FAIL 1/1 — see {basePath}.md");
        var markdown = await File.ReadAllTextAsync(basePath + ".md", TestContext.Current.CancellationToken);
        markdown.ShouldContain("bad-image-label");
        markdown.ShouldContain("compute.start");
        markdown.ShouldContain("no such image");
    }

    [Fact(DisplayName = "Given a report, when written, then the JSON and markdown reflect the same summary counts")]
    public async Task KeepJsonAndMarkdownInSyncAsync()
    {
        var report = RunReport.FromResults(
            "agent-loop",
            "fake",
            DateTimeOffset.UtcNow,
            TimeSpan.FromSeconds(2),
            [
                ScenarioResult.Success("a", TimeSpan.FromSeconds(1), []),
                ScenarioResult.Failure("b", "assertions.run", "final status was 'Failed', expected 'succeeded'", TimeSpan.FromSeconds(1), []),
            ]);
        var basePath = Path.Combine(directory, "sync-report");

        await ReportWriter.WriteAsync(report, basePath, TestContext.Current.CancellationToken);

        var json = await File.ReadAllTextAsync(basePath + ".json", TestContext.Current.CancellationToken);
        var markdown = await File.ReadAllTextAsync(basePath + ".md", TestContext.Current.CancellationToken);
        json.ShouldContain("\"total\": 2");
        json.ShouldContain("\"passed\": 1");
        json.ShouldContain("\"failed\": 1");
        markdown.ShouldContain("|2|1|1|0|");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
