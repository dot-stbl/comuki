using Comuki.AgentTest.Runner.Execution;
using Comuki.AgentTest.Runner.Reporting;
using Comuki.AgentTest.Runner.Scenarios;
using Shouldly;
using Xunit;

namespace Comuki.EndToEnd.AgentLoop;

/// <summary>
/// T2a (design.md D3) proven end to end: a real webhook seeds a run/work
/// item, a real <see cref="Engine.Compute.Providers.DockerComputeProvider"/>
/// provisions a real Podman container running the WS6 test worker image
/// (<c>TestFakePi</c> standing in for <c>pi</c>), the container's real
/// Translator claims over REST and streams over the real gRPC bidi
/// channel, and the journal records it — no model call anywhere in this
/// suite. <see cref="ScenarioRunner"/> +
/// <see cref="ReportWriter"/> are the
/// exact same classes WS7/WS8/WS9 extend.
/// </summary>
[Collection(nameof(AgentLoopCollection))]
public sealed class AgentLoopScenarioShould(AgentLoopHost host)
{
    [Fact(
        DisplayName = "Given the add-null-check scenario, when it runs T2a against a real container, then it passes and the report shows PASS 1/1")]
    public async Task RunAddNullCheckScenarioAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = ScenarioLoader.Load(ScenarioPath("add-null-check.scenario.yaml"));
        var runner = new ScenarioRunner(new AgentLoopHarness(host));

        var result = await runner.RunAsync(
            scenario,
            new ScenarioRunOptions { ArtifactsDirectory = ArtifactsDirectory() },
            cancellationToken);

        if (!result.Passed)
        {
            throw new Xunit.Sdk.XunitException($"[{result.FailedStage}] {result.FailureMessage}");
        }

        var report = RunReport.FromResults("agent-loop", "fake", DateTimeOffset.UtcNow, result.Duration, [result]);
        var verdict = await ReportWriter.WriteAsync(report, Path.Combine(ArtifactsDirectory(), "add-null-check-report"), cancellationToken);
        verdict.ShouldBe("PASS 1/1");
    }

    [Fact(DisplayName = "Given a scenario whose worker.image was never built, when the runner tries to start the container, then it reports a named compute.start failure in the report, not a stack trace")]
    public async Task ReportBadImageLabelAsCleanFailureAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = ScenarioLoader.Load(ScenarioPath("bad-image-label.scenario.yaml"));
        var runner = new ScenarioRunner(new AgentLoopHarness(host));

        var result = await runner.RunAsync(
            scenario,
            new ScenarioRunOptions { ArtifactsDirectory = ArtifactsDirectory() },
            cancellationToken);

        result.Passed.ShouldBeFalse("a nonexistent worker.image tag must fail the compute-start step");
        result.FailedStage.ShouldBe("compute.start");
        result.FailureMessage.ShouldNotBeNullOrWhiteSpace();
        result.FailureMessage!.Contains("\n   at ", StringComparison.Ordinal)
            .ShouldBeFalse("the failure message must name the problem, not dump a raw .NET stack trace");

        var report = RunReport.FromResults("agent-loop", "fake", DateTimeOffset.UtcNow, result.Duration, [result]);
        var reportBasePath = Path.Combine(ArtifactsDirectory(), "bad-image-label-report");
        var verdict = await ReportWriter.WriteAsync(report, reportBasePath, cancellationToken);

        verdict.ShouldBe($"FAIL 1/1 — see {reportBasePath}.md");
        report.Failures.ShouldHaveSingleItem();
        report.Failures[0].Scenario.ShouldBe("bad-image-label");
        report.Failures[0].Stage.ShouldBe("compute.start");

        var markdown = await File.ReadAllTextAsync(reportBasePath + ".md", cancellationToken);
        markdown.ShouldContain("bad-image-label");
        markdown.ShouldContain("compute.start");
    }

    private static string ScenarioPath(string fileName)
    {
        return Path.Combine(RepositoryRoot(), "tests", "fixtures", "scenarios", "small-repo", fileName);
    }

    private static string ArtifactsDirectory()
    {
        return Path.Combine(AppContext.BaseDirectory, "artifacts", "agent-loop");
    }

    private static string RepositoryRoot()
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
