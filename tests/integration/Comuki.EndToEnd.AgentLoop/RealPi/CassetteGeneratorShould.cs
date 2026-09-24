using Comuki.AgentTest.Runner.Execution;
using Comuki.AgentTest.Runner.Scenarios;
using Comuki.TestFakeModel.Cassettes;
using Comuki.TestFakeModel.Cassettes.Hosting;
using Comuki.TestFakeModel.Cassettes.IO;
using Shouldly;
using Xunit;

namespace Comuki.EndToEnd.AgentLoop.RealPi;

/// <summary>
/// One-shot cassette generator for <see cref="ReplayPiFakeModelScenarioShould"/>'s
/// committed fixture. Records a real, vendored <c>pi</c> binary running the
/// WS7 <c>add-null-check</c> ticket/expected-trajectory through a recording
/// <see cref="CassetteModelServer"/> (Mode=Record) whose upstream is a
/// scripted <c>FakeModelServer</c> — never a paid API, never an outbound
/// network call. The redacted cassette at <c>cassettes/add-null-check.v1.json</c>
/// is what <see cref="ReplayPiFakeModelHarness"/> then serves back to
/// replay-mode runs forever, no live upstream needed.
/// </summary>
/// <remarks>
/// The <c>Skip</c> string names the env var that re-runs this fact — set
/// <c>COMUKI_REGENERATE_CASSETTE=1</c> for one run after a fakescript
/// change and the temp cassette overwrites the committed fixture. Re-run
/// is intentionally hand-rolled here (not a <c>scripts/ci/*.mjs</c>
/// workflow) because the generator exercises the same real-pi + Postgres
/// stack the replay test does, just with one extra process in the chain —
/// the cheapest possible re-record path is "rerun this same fact".
/// </remarks>
[Collection(nameof(RealPiFakeModelCollection))]
public sealed class CassetteGeneratorShould(RealPiInstallation realPi, RealPiFakeModelHost host)
{
    private const string CommittedCassetteRelativePath = "cassettes/add-null-check.v1.json";

    [Fact(Skip = "one-time cassette generator — re-record add-null-check.v1.json by setting COMUKI_REGENERATE_CASSETTE=1 "
        + "before running this test. The fact was used once to commit the initial cassette fixture for WS8; after "
        + "that the Skip keeps CI runs from re-running it. Re-running this fact exercises the same real-pi + "
        + "Postgres stack as the replay test itself, just with one extra process in the chain — the cheapest "
        + "possible re-record path. The committed cassette was recorded against a scripted FakeModelServer via "
        + "a recording CassetteModelServer (Mode=Record) — never a paid API, never a network call — and was "
        + "inspected for schemaVersion: 1, exactly 2 exchanges, and zero [redacted] placeholders before being "
        + "promoted to the committed path.")]
    public async Task GenerateAddNullCheckCassetteAsync()
    {
        if (Environment.GetEnvironmentVariable("COMUKI_REGENERATE_CASSETTE") != "1")
        {
            throw new Xunit.Sdk.XunitException(
                "CassetteGeneratorShould is intentionally skipped — set COMUKI_REGENERATE_CASSETTE=1 to refresh the "
                + "committed add-null-check.v1.json cassette (see test [Fact(Skip = ...)] for the full instruction).");
        }

        var cancellationToken = TestContext.Current.CancellationToken;

        // Write to a fresh temp file first, inspect it, then copy — a bad
        // run never corrupts the committed fixture in git history.
        var tempCassettePath = Path.Combine(
            Path.GetTempPath(),
            $"comuki-cassette-gen-{Guid.NewGuid():N}.json");

        // Use the WS7 scenario (model.mode: fake + fakeScript) — its
        // ticket/fixture/repo copy are exactly the trajectory we want
        // recorded. The recording harness swaps the FakeModelServer for a
        // FakeModelServer → recording-CassetteModelServer chain and points
        // real pi at the cassette server's BaseAddress instead.
        var recordedScenario = ScenarioLoader.Load(ScenarioPath("add-null-check-real-pi.scenario.yaml"));

        var harness = new CassetteGeneratingPiFakeModelHarness(realPi, host, tempCassettePath);
        await using (harness)
        {
            var runner = new ScenarioRunner(harness);
            var result = await runner.RunAsync(
                recordedScenario,
                new ScenarioRunOptions { Timeout = TimeSpan.FromSeconds(150) },
                cancellationToken);

            if (!result.Passed)
            {
                throw new Xunit.Sdk.XunitException(
                    $"cassette generation run failed (the recording proxy should record the same two-turn exchange the "
                    + $"fakeScript scripts — a failure here means the redaction or record-mode wiring regressed): "
                    + $"[{result.FailedStage}] {result.FailureMessage}");
            }
        }

        // Inspect the temp cassette before promoting it to the committed path.
        // The brief is explicit: "confirm exactly 2 exchanges, confirm every
        // value on the redaction allowlist round-tripped (no [redacted] where
        // real content like the edit tool_use block should be — only genuinely
        // secret-shaped strings, if any, should be redacted), confirm
        // schemaVersion: 1."
        var produced = CassetteReader.LoadFromFile(tempCassettePath);
        produced.SchemaVersion.ShouldBe(CassetteFile.CurrentSchemaVersion);
        produced.Exchanges.Count.ShouldBe(
            2,
            "the add-null-check fakeScript scripts exactly two turns (initial edit + follow-up end_turn); the recording should match");

        var rawJson = await File.ReadAllTextAsync(tempCassettePath, cancellationToken);
        var placeholderCount = CountOccurrences(rawJson, "[redacted]");
        placeholderCount.ShouldBe(
            0,
            "no secret-shaped leaf values should be redacted in this fixture — the recording upstream is a scripted fake model, not a paid API call, so nothing reaches the recorder that triggers SecretPatterns.LooksLikeSecret. Any non-zero count means a redaction regression.");

        // Promote temp → committed. The directory creation mirrors
        // ScenarioLoader.ResolveRelativeToScenario's own directory handling
        // so the committed cassette lives where
        // ReplayPiFakeModelScenarioShould + add-null-check-replay.scenario.yaml
        // will look for it.
        var committedCassettePath = Path.Combine(ScenarioPath(""), CommittedCassetteRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(committedCassettePath)!);
        File.Copy(tempCassettePath, committedCassettePath, overwrite: true);
        File.Delete(tempCassettePath);
    }

    private static string ScenarioPath(string fileName)
    {
        return Path.Combine(RepositoryRoot(), "tests", "fixtures", "scenarios", "small-repo", fileName);
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

    /// <summary>Non-overlapping count of <paramref name="needle"/> in <paramref name="haystack"/>, scanning via <see cref="string.IndexOf(string, int, StringComparison)"/>.</summary>
    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var cursor = 0;
        while ((cursor = haystack.IndexOf(needle, cursor, StringComparison.Ordinal)) >= 0)
        {
            count++;
            cursor += needle.Length;
        }

        return count;
    }
}
