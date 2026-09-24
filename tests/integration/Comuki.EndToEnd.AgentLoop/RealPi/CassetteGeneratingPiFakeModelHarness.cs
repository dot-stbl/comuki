using Comuki.AgentTest.Runner.Scenarios;
using Comuki.TestFakeModel.Cassettes.Hosting;
using Comuki.TestFakeModel.Hosting;
using Comuki.TestFakeModel.Scripting.Loading;

namespace Comuki.EndToEnd.AgentLoop.RealPi;

/// <summary>
/// One-shot harness for regenerating the <c>add-null-check.v1.json</c>
/// cassette used by <see cref="ReplayPiFakeModelScenarioShould"/>. WS8's
/// "produce a real, recorded cassette" half — starts a scripted
/// <see cref="FakeModelServer"/> as the upstream, points a recording
/// <see cref="CassetteModelServer"/> (Mode=Record) at it, and redirects
/// real pi at the recording server's BaseAddress; every real-pi model
/// call gets forwarded through to the scripted fake, the upstream's
/// response (already redacted by <c>Cassettes.Redaction.CassetteRedactor</c>
/// before a byte is written) lands in the recording cassette. Never paid
/// API, never real network — the recording server is a local proxy just
/// for the run that produces this fixture. Lives as a
/// <c>[Fact(Skip = "...")]</c>-guarded generator in
/// <see cref="CassetteGeneratorShould"/> rather than being deleted: a
/// future maintainer can rerun it with the env var described there to
/// refresh the cassette without needing <c>scripts/ci/record-cassette.mjs</c>
/// (which does not exist on this branch yet — a sibling chunk adds it
/// later and will reuse this same recording pattern).
/// </summary>
internal sealed class CassetteGeneratingPiFakeModelHarness(RealPiInstallation realPi, RealPiFakeModelHost host, string cassettePath) : RealPiHarnessBase(realPi, host)
{
    private FakeModelServer? fakeModelServer;
    private CassetteModelServer? cassetteServer;

    /// <inheritdoc />
    protected override async Task<Uri> StartModelServerAsync(ScenarioDefinition scenario, CancellationToken cancellationToken)
    {
        // Same shape as the existing fake-script guard on
        // RealPiFakeModelHarness — the cassette's recording upstream must
        // be a scripted fake model, not the real upstream.
        if (scenario.Model is not { FakeScript: { Length: > 0 } fakeScriptRelativePath })
        {
            throw new InvalidOperationException(
                $"cassette generator scenario '{scenario.Name}' declares no model.fakeScript — the recording proxy must point at a scripted fake model upstream.");
        }

        var fakeScriptPath = ScenarioLoader.ResolveRelativeToScenario(scenario.SourcePath, fakeScriptRelativePath);
        var script = FakeScriptLoader.LoadFromFile(fakeScriptPath, scenario.Name);
        fakeModelServer = new FakeModelServer(new FakeModelServerOptions { Script = script });
        await fakeModelServer.StartAsync(cancellationToken);

        // The recording proxy stands between real pi and the scripted fake.
        // real pi's models.json baseUrl points at the proxy here, NOT at the
        // fake directly — that is the entire point of the generator.
        cassetteServer = new CassetteModelServer(new CassetteModelServerOptions
        {
            Mode = CassetteModelMode.Record,
            CassettePath = cassettePath,
            Scenario = scenario.Name,
            RecordedAgainst = "fake-script-via-recording-proxy",
            UpstreamBaseUrl = fakeModelServer.BaseAddress,
        });
        await cassetteServer.StartAsync(cancellationToken);

        return cassetteServer.BaseAddress;
    }

    /// <inheritdoc />
    protected override async ValueTask DisposeModelServerAsync()
    {
        // Dispose order matters: stop accepting traffic on the recording
        // proxy first (so the fake is no longer reachable through it), then
        // dispose the fake. Otherwise the cassette writer's last flush could
        // race against an in-flight request against an already-disposed
        // fake's Kestrel thread.
        if (cassetteServer is not null)
        {
            await cassetteServer.DisposeAsync();
            cassetteServer = null;
        }

        if (fakeModelServer is not null)
        {
            await fakeModelServer.DisposeAsync();
            fakeModelServer = null;
        }
    }
}
