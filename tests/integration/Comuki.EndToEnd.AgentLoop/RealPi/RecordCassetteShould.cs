using Comuki.AgentTest.Runner.Execution;
using Comuki.AgentTest.Runner.Scenarios;
using Shouldly;
using Xunit;

namespace Comuki.EndToEnd.AgentLoop.RealPi;

/// <summary>
/// Generic, env-var-activated <c>record-cassette</c> driver for
/// <c>scripts/ci/record-cassette.mjs</c>. No-op by default — runs a real-pi
/// scenario against a <see cref="RecordingPiFakeModelHarness"/> only when
/// <c>COMUKI_RECORD_SCENARIO</c> and <c>COMUKI_RECORD_UPSTREAM</c> are both
/// set, so ordinary <c>dotnet run</c> of this suite never picks it up
/// (and every pre-existing fact in <see cref="RealPiFakeModelCollection"/>
/// is unaffected — see WS8 task 8.2 acceptance).
/// </summary>
/// <remarks>
/// <para>
/// Environment variables (all read at fact entry):
/// <list type="bullet">
///   <item><c>COMUKI_RECORD_SCENARIO</c> — absolute path to the scenario YAML
/// to run. Required.</item>
///   <item><c>COMUKI_RECORD_UPSTREAM</c> — absolute URL the recording proxy
/// forwards each request to. Required.</item>
///   <item><c>COMUKI_RECORD_CASSETTE_OUT</c> — absolute path to write the
/// new cassette to. Optional; defaults to the scenario's declared
/// <c>model.cassette</c> resolved relative to the scenario file's own
/// directory (same rule <see cref="ScenarioLoader.ResolveRelativeToScenario"/>
/// applies on the replay side).</item>
///   <item><c>COMUKI_RECORD_BUDGET_USD</c> — optional decimal string; the
/// future <c>Comuki.AgentTest.Runner</c> shared budget tracker (WS9) will
/// enforce it mid-run. For now the fact echoes the budget to the test
/// output as a one-line note and continues — no enforcement lands until
/// WS9 wires the shared tracker.</item>
/// </list>
/// </para>
/// <para>
/// Designed so the existing <c>CassetteGeneratorShould</c>
/// (<c>COMUKI_REGENERATE_CASSETTE=1</c>) is left intact: that fact is a
/// hand-rolled one-off for the committed <c>add-null-check.v1.json</c>
/// cassette and stays as a <c>[Fact(Skip = "...")]</c>-guarded generator;
/// this new fact is the CI-script-driven path a maintainer re-records any
/// scenario's cassette through.
/// </para>
/// </remarks>
[Collection(nameof(RealPiFakeModelCollection))]
public sealed class RecordCassetteShould(RealPiInstallation realPi, RealPiFakeModelHost host)
{
    private const string ScenarioEnvVar = "COMUKI_RECORD_SCENARIO";
    private const string UpstreamEnvVar = "COMUKI_RECORD_UPSTREAM";
    private const string CassetteOutEnvVar = "COMUKI_RECORD_CASSETTE_OUT";
    private const string BudgetEnvVar = "COMUKI_RECORD_BUDGET_USD";

    [Fact(DisplayName = "Given COMUKI_RECORD_SCENARIO + COMUKI_RECORD_UPSTREAM, when the record driver runs, then a fresh cassette is written at the resolved path through a real-pi translator cycle")]
    public async Task RecordCassetteForScenarioAsync()
    {
        var scenarioPath = Environment.GetEnvironmentVariable(ScenarioEnvVar);
        var upstreamRaw = Environment.GetEnvironmentVariable(UpstreamEnvVar);
        var cassetteOutRaw = Environment.GetEnvironmentVariable(CassetteOutEnvVar);
        var budgetRaw = Environment.GetEnvironmentVariable(BudgetEnvVar);

        Assert.SkipUnless(
            !string.IsNullOrWhiteSpace(scenarioPath) && !string.IsNullOrWhiteSpace(upstreamRaw),
            $"record-cassette driver is opt-in via {ScenarioEnvVar} + {UpstreamEnvVar}; both must be present to activate. "
                + "Set both to drive a one-shot re-record through this suite (see scripts/ci/record-cassette.mjs).");

        // Boundary: Assert.SkipUnless returns void — it doesn't narrow the
        // nullability of the captured env-var locals in the compiler's view.
        // ` ?? throw` collapses the two remaining nullable references to
        // non-null for the body without leaking `!` to the rest of the file.
        var scenarioPathNonNull = scenarioPath ?? throw new InvalidOperationException(
            $"Assert.SkipUnless already gated this fact on {ScenarioEnvVar} being set");
        var upstreamNonNull = upstreamRaw ?? throw new InvalidOperationException(
            $"Assert.SkipUnless already gated this fact on {UpstreamEnvVar} being set");

        if (!Uri.TryCreate(upstreamNonNull, UriKind.Absolute, out var upstreamBaseUrl))
        {
            throw new InvalidOperationException(
                $"{UpstreamEnvVar}='{upstreamNonNull}' is not a valid absolute URL");
        }

        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = ScenarioLoader.Load(scenarioPathNonNull);

        var cassettePath = !string.IsNullOrWhiteSpace(cassetteOutRaw)
            ? Path.GetFullPath(cassetteOutRaw)
            : !string.IsNullOrWhiteSpace(scenario.Model?.Cassette)
                ? ScenarioLoader.ResolveRelativeToScenario(scenario.SourcePath, scenario.Model.Cassette)
                : throw new InvalidOperationException(
                    $"record-cassette: scenario '{scenario.Name}' declares no model.cassette and {CassetteOutEnvVar} is unset — cannot resolve output path. "
                    + "Either add `cassette: <path>` to the scenario's model: block, or pass --cassette-out / " + CassetteOutEnvVar + ".");

        // Echo the budget before the run, so a human re-recording against
        // a paid upstream sees what they asked for — even before WS9 wires
        // real mid-run enforcement.
        if (!string.IsNullOrWhiteSpace(budgetRaw))
        {
            // TODO(WS9): wire real budget enforcement here — the upcoming
            // shared BudgetTracker in Comuki.AgentTest.Runner is the
            // planned integration point; for now this is plumbing only.
            Console.Out.WriteLine($"record-cassette: budget requested = ${budgetRaw} (not yet enforced — WS9)");
        }

        var harness = new RecordingPiFakeModelHarness(
            realPi,
            host,
            cassettePath,
            upstreamBaseUrl,
            recordedAgainst: $"recorded-upstream-{upstreamBaseUrl.Host}");
        await using (harness)
        {
            var runner = new ScenarioRunner(harness);
            var result = await runner.RunAsync(
                scenario,
                new ScenarioRunOptions { Timeout = TimeSpan.FromSeconds(150) },
                cancellationToken);

            if (!result.Passed)
            {
                throw new Xunit.Sdk.XunitException(
                    $"record-cassette run failed (the recording proxy should forward the same scripted fake's exchange the fakeScript scripts — "
                    + $"a failure here means the redaction or record-mode wiring regressed): [{result.FailedStage}] {result.FailureMessage}");
            }
        }

        // Final sanity: the cassette file the upstream flow is supposed to
        // have written must exist on disk after the run. record-cassette.mjs
        // does its own JSON shape check; this is just the in-test side of
        // the same invariant so a human running the suite directly also
        // sees the problem if the recording path silently no-ops.
        File.Exists(cassettePath).ShouldBeTrue(
            $"record-cassette driver finished its run but no cassette file exists at '{cassettePath}' — the recording path silently no-op'd");
    }
}
