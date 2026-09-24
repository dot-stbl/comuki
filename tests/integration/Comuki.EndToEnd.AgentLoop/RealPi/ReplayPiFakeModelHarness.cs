using Comuki.AgentTest.Runner.Execution;
using Comuki.AgentTest.Runner.Scenarios;
using Comuki.TestFakeModel.Cassettes.Hosting;

namespace Comuki.EndToEnd.AgentLoop.RealPi;

/// <summary>
/// T2b's <c>model.mode: replay</c> <see cref="IAgentLoopHarness"/> (WS8 —
/// design.md D3): the same <see cref="RealPiHarnessBase"/> scaffolding
/// <see cref="RealPiFakeModelHarness"/> uses, but the model server this
/// harness starts is a <see cref="CassetteModelServer"/> in
/// <see cref="CassetteModelMode.Replay"/> mode, served byte-for-byte from
/// a committed cassette — the WS8 acceptance that the recorded
/// fake-script → real-pi → real-edit loop round-trips against the same
/// fixture with no live network involved (replay mode never calls out;
/// see <c>Cassettes.Replay.CassettePlaybackState</c> and
/// <c>Cassettes.Endpoints.CassetteReplayEndpoint</c> for the proof).
/// </summary>
/// <remarks>
/// A separate harness from <see cref="RealPiFakeModelHarness"/> rather
/// than a mode flag on it: <c>FakeModelServer</c> and
/// <c>CassetteModelServer</c> are different types (different base classes,
/// different startup shapes), and each harness keeps its own server
/// reference to expose any harness-specific assertions later without
/// dragging in unused mode branches. The ~90% shared scaffolding lives
/// in <see cref="RealPiHarnessBase"/>; only <see cref="StartModelServerAsync"/>
/// varies.
/// </remarks>
public sealed class ReplayPiFakeModelHarness : RealPiHarnessBase
{
    private CassetteModelServer? cassetteServer;

    /// <inheritdoc />
    public ReplayPiFakeModelHarness(RealPiInstallation realPi, RealPiFakeModelHost host)
        : base(realPi, host)
    {
    }

    /// <inheritdoc />
    protected override async Task<Uri> StartModelServerAsync(ScenarioDefinition scenario, CancellationToken cancellationToken)
    {
        if (scenario.Model is not { Mode: ScenarioModelMode.Replay, Cassette: { Length: > 0 } cassetteRelativePath })
        {
            throw new InvalidOperationException(
                $"scenario '{scenario.Name}' does not declare model.mode: replay with a model.cassette — ReplayPiFakeModelHarness has no cassette to serve.");
        }

        var cassettePath = ScenarioLoader.ResolveRelativeToScenario(scenario.SourcePath, cassetteRelativePath);
        if (!File.Exists(cassettePath))
        {
            throw new InvalidOperationException(
                $"scenario '{scenario.Name}' declares model.cassette '{cassetteRelativePath}', but no file exists at '{cassettePath}'");
        }

        cassetteServer = new CassetteModelServer(new CassetteModelServerOptions
        {
            Mode = CassetteModelMode.Replay,
            CassettePath = cassettePath,
        });
        await cassetteServer.StartAsync(cancellationToken);

        return cassetteServer.BaseAddress;
    }

    /// <inheritdoc />
    protected override async ValueTask DisposeModelServerAsync()
    {
        if (cassetteServer is not null)
        {
            await cassetteServer.DisposeAsync();
            cassetteServer = null;
        }
    }
}
