using Comuki.AgentTest.Runner.Execution;
using Comuki.AgentTest.Runner.Reporting;
using Comuki.AgentTest.Runner.Scenarios;
using Shouldly;
using Xunit;

namespace Comuki.EndToEnd.AgentLoop.RealPi;

/// <summary>
/// WS9 live-mode driver — the same env-var-activated shape as
/// <see cref="RecordCassetteShould"/>: skipped by default, runs only when
/// the caller has pointed <c>COMUKI_LIVE_MODEL_BASE_URL</c> at a real
/// upstream (which, by the parent brief, is never a paid API in any gate
/// this agent runs — always a <c>FakeModelServer</c> on loopback).
/// </summary>
/// <remarks>
/// <para>Environment variables consumed:
/// <list type="bullet">
///   <item><c>COMUKI_LIVE_MODEL_BASE_URL</c> — required to activate; the URL
/// the recording <c>CassetteModelServer</c> forwards every inbound request
/// to (a fake <c>FakeModelServer</c> on a loopback port in any
/// gate this driver will ever see).</item>
///   <item><c>COMUKI_LIVE_MODEL_TOKEN</c> — optional bearer token stamped
/// via <c>ANTHROPIC_AUTH_TOKEN</c>; an unset value just stamps a
/// placeholder string the same way <see cref="RealPiHarnessBase"/>'s own
/// stamp does.</item>
///   <item><c>COMUKI_LIVE_BUDGET_MAX_USD</c> — optional global ceiling that
/// combines with <c>scenario.budget.maxUsd</c> via
/// <c>BudgetCap.Resolve</c>; whichever is smaller wins. Sets the harness's
/// per-run <c>BudgetCap</c>.</item>
///   <item><c>COMUKI_LIVE_CASSETTE_PATH</c> — optional absolute path; the
/// recording side writes a transient cassette here. A live run's cassette
/// is intentionally NOT a fixture (a live run's transcript is not a
/// fixture; only <c>scripts/ci/record-cassette.mjs</c>'s explicit,
/// human-reviewed re-record flow produces committed cassettes).</item>
/// </list>
/// </para>
/// <para>
/// Gate-running semantics: by default (no env) this fact skips. When the
/// base URL is set, the harness runs end-to-end against whatever the
/// caller pointed it at; a per-invocation fresh server is enforced by the
/// shared <see cref="RealPiFakeModelCollection"/>'s port allocation, never
/// against a server cached across fact invocations.
/// </para>
/// </remarks>
[Collection(nameof(RealPiFakeModelCollection))]
public sealed class LiveModeScenarioShould(RealPiInstallation realPi, RealPiFakeModelHost host)
{
    private const string LiveModelBaseUrlEnvVar = "COMUKI_LIVE_MODEL_BASE_URL";
    private const string LiveBudgetEnvVar = "COMUKI_LIVE_BUDGET_MAX_USD";
    private const string LiveCassettePathEnvVar = "COMUKI_LIVE_CASSETTE_PATH";

    /// <summary>
    /// Optional absolute base path (no extension) the JSON+markdown report
    /// (design.md "Report format for agents") is written to — consumed by
    /// <c>scripts/ci/live-eval.mjs</c>, which sets this to a scratch path it
    /// controls so it can read the report back after the run. Defaults to
    /// <see cref="ArtifactsDirectory"/> when unset, same as the sibling
    /// real-pi facts.
    /// </summary>
    private const string LiveReportPathEnvVar = "COMUKI_LIVE_REPORT_PATH";

    [Fact(DisplayName = "Given COMUKI_LIVE_MODEL_BASE_URL, when the live harness runs, then a real-pi translator cycle makes it to terminal Succeeded against the pointed-at upstream")]
    public async Task LiveRunReachesTerminalSucceededAgainstPointedUpstreamAsync()
    {
        var baseUrlRaw = Environment.GetEnvironmentVariable(LiveModelBaseUrlEnvVar);
        var budgetRaw = Environment.GetEnvironmentVariable(LiveBudgetEnvVar);
        var cassettePathRaw = Environment.GetEnvironmentVariable(LiveCassettePathEnvVar);
        var reportPathRaw = Environment.GetEnvironmentVariable(LiveReportPathEnvVar);

        Assert.SkipUnless(
            !string.IsNullOrWhiteSpace(baseUrlRaw),
            $"{LiveModelBaseUrlEnvVar} not set — live mode skipped. Set the var (pointing at a FakeModelServer / stub listener, never a paid API) to activate this fact.");

        var cancellationToken = TestContext.Current.CancellationToken;

        var scenario = ScenarioLoader.Load(ScenarioPath("add-null-check-live.scenario.yaml"));

        var cassettePath = !string.IsNullOrWhiteSpace(cassettePathRaw)
            ? Path.GetFullPath(cassettePathRaw)
            : Path.Combine(Path.GetTempPath(), $"comuki-live-{scenario.Name}-{Guid.NewGuid():N}.json");

        var scenarioBudgetMaxUsd = scenario.Budget?.MaxUsd;
        var cap = LiveModePiFakeModelHarness.BuildCap(scenarioBudgetMaxUsd, budgetRaw);

        var harness = new LiveModePiFakeModelHarness(realPi, host, cap, cassettePath);
        await using (harness)
        {
            var runner = new ScenarioRunner(harness);
            var result = await runner.RunAsync(
                scenario,
                new ScenarioRunOptions { ArtifactsDirectory = ArtifactsDirectory(), Timeout = TimeSpan.FromSeconds(180) },
                cancellationToken);

            if (!result.Passed)
            {
                throw new Xunit.Sdk.XunitException(
                    $"live-mode run failed (this fact only exercises the wiring — failure modes that surface here are real, not 'no live env set'): "
                    + $"[{result.FailedStage}] {result.FailureMessage}");
            }

            // Cost thread: a successful live run reports the observed
            // USD-micros / token counts through to the report aggregation —
            // surfaces that the half-A wiring actually carries the per-run
            // spend end-to-end (zero is a valid value when the upstream
            // returns no tokens).
            result.Cost.ShouldNotBeNull();
            result.Cost.UsdMicros.ShouldBeGreaterThanOrEqualTo(0);

            // Same JSON+markdown report shape every tier writes (design.md
            // "Report format for agents") — live-eval.mjs reads this file
            // back after the dotnet run exits, rather than re-deriving a
            // summary from raw test-runner stdout.
            var reportBasePath = !string.IsNullOrWhiteSpace(reportPathRaw)
                ? Path.GetFullPath(reportPathRaw)
                : Path.Combine(ArtifactsDirectory(), "add-null-check-live-report");
            var report = RunReport.FromResults("agent-loop", "live", DateTimeOffset.UtcNow, result.Duration, [result]);
            await ReportWriter.WriteAsync(report, reportBasePath, cancellationToken);
        }
    }

    private static string ScenarioPath(string fileName)
    {
        return Path.Combine(RepositoryRoot(), "tests", "fixtures", "scenarios", "small-repo", fileName);
    }

    private static string ArtifactsDirectory()
    {
        return Path.Combine(AppContext.BaseDirectory, "artifacts", "real-pi-live");
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
