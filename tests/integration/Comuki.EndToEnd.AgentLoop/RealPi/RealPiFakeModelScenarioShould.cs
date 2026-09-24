using Comuki.AgentTest.Runner.Execution;
using Comuki.AgentTest.Runner.Reporting;
using Comuki.AgentTest.Runner.Scenarios;
using Shouldly;
using Xunit;

namespace Comuki.EndToEnd.AgentLoop.RealPi;

/// <summary>
/// T2b (design.md D3) proven end to end: the real, vendored <c>pi</c>
/// binary (deploy/hybrid/vendor/earendil-works-pi-coding-agent-0.85.1.tgz)
/// runs in-process against <c>Comuki.TestFakeModel</c>'s
/// <c>FakeModelServer</c>, scripted to edit the fixture repo's bug — the
/// first test anywhere that observes a real pi process (and, once
/// harden-pi-worker-sandbox 6.1 lands, the <c>agents/comuki-worker-sdk</c>
/// pi-extension) driven by a real model-shaped HTTP surface, not
/// <c>TestFakePi</c>'s scripted process-level stream-json.
/// </summary>
[Collection(nameof(RealPiFakeModelCollection))]
public sealed class RealPiFakeModelScenarioShould(RealPiInstallation realPi, RealPiFakeModelHost host)
{
    [Fact(DisplayName = "Given the add-null-check scenario, when the real pi binary runs against a scripted fake model via PI_CODING_AGENT_DIR/models.json, then it edits the fixture, the fake model observes both turns, and the run succeeds")]
    public async Task RunAddNullCheckScenarioWithRealPiAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = ScenarioLoader.Load(ScenarioPath("add-null-check-real-pi.scenario.yaml"));

        var harness = new RealPiFakeModelHarness(realPi, host);
        await using (harness)
        {
            var runner = new ScenarioRunner(harness);
            var result = await runner.RunAsync(
                scenario,
                new ScenarioRunOptions { ArtifactsDirectory = ArtifactsDirectory(), Timeout = TimeSpan.FromSeconds(150) },
                cancellationToken);

            if (!result.Passed)
            {
                throw new Xunit.Sdk.XunitException($"[{result.FailedStage}] {result.FailureMessage}");
            }

            var report = RunReport.FromResults("agent-loop", "fake", DateTimeOffset.UtcNow, result.Duration, [result]);
            var verdict = await ReportWriter.WriteAsync(report, Path.Combine(ArtifactsDirectory(), "add-null-check-real-pi-report"), cancellationToken);
            verdict.ShouldBe("PASS 1/1");

            // Beyond ScenarioRunner's own journal/trajectory/diff assertions
            // (all already checked above): directly prove the fake model
            // itself received the requests real pi actually sent — WS7's
            // T2b acceptance ("prove whether requests reach the fake
            // model" / issue #150's core question).
            var requests = harness.FakeModelRequests;
            requests.Count.ShouldBeGreaterThanOrEqualTo(2, "the scripted edit turn plus the follow-up turn carrying the tool_result");
            requests[0].HasToolResult.ShouldBeFalse("the first request is the initial prompt, before any tool ran");
            requests.ShouldContain(request => request.HasToolResult, "a later request must carry the edit tool's tool_result — proof the fake model's tool_use round-tripped through a real pi tool execution, not just a scripted echo");
            requests.ShouldAllBe(
                static request => request.Path == "/v1/messages",
                "real pi must speak the Anthropic Messages wire shape (the models.json override only redirects the anthropic provider, not any other)");
        }
    }

    /// <summary>
    /// WS7 task 7.2's documented-but-unenforced half: <c>expectedTrajectory[].forbiddenTools</c>
    /// today only proves a forbidden tool was never <em>used</em> (the
    /// fakeScript never asks pi to call one) — it does not prove pi
    /// <em>couldn't</em> call one. That stronger claim needs
    /// <c>agents/comuki-worker-sdk</c>'s pi-extension lock/allowlist gate
    /// (harden-pi-worker-sandbox task 6.1), which has not landed yet
    /// (tracked in gh-issue #125, item 7 — "pi-extensions"). Per WS7's own
    /// task description: written against the extension's documented
    /// contract, marked pending rather than reimplementing lock enforcement
    /// here or silently omitting the assertion.
    /// </summary>
    [Fact(Skip = "blocked on harden-pi-worker-sandbox 6.1 (agents/comuki-worker-sdk pi-extension lock/allowlist gate), gh-issue #125 item 7. "
        + "Contract this will assert once 6.1 lands: script the fake model to return a tool_use block naming a tool "
        + "outside the worker-sdk's allowlist (e.g. WebFetch); assert the pi-extension either (a) never lets pi emit "
        + "the tool_use in the first place (no matching tool_execution_start on the timeline), or (b) intercepts it "
        + "and reports a denial back to pi as the tool_result (tool_execution_end with isError: true and a "
        + "lock/allowlist-denial message) rather than letting it execute — either shape proves enforcement; "
        + "TrajectoryAssertionEvaluator's ForbiddenTools check alone (which this suite's other fact already exercises) "
        + "cannot distinguish 'never asked' from 'asked and blocked', which is exactly the gap this pending test closes.")]
    public Task EnforceForbiddenToolAgainstRealWorkerSdkLockAsync()
    {
        return Task.CompletedTask;
    }

    private static string ScenarioPath(string fileName)
    {
        return Path.Combine(RepositoryRoot(), "tests", "fixtures", "scenarios", "small-repo", fileName);
    }

    private static string ArtifactsDirectory()
    {
        return Path.Combine(AppContext.BaseDirectory, "artifacts", "real-pi");
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
