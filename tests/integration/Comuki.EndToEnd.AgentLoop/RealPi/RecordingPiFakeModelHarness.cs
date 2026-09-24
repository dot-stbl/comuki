using Comuki.AgentTest.Runner.Reporting.Report;
using Comuki.AgentTest.Runner.Scenarios;
using Comuki.TestFakeModel.Cassettes.Hosting;

namespace Comuki.EndToEnd.AgentLoop.RealPi;

/// <summary>
/// Generic, upstream-driven <see cref="RealPiHarnessBase"/> subclass for
/// WS8's <c>scripts/ci/record-cassette.mjs</c> re-record command: starts a
/// <see cref="CassetteModelServer"/> in <see cref="CassetteModelMode.Record"/>
/// pointing at a caller-provided upstream URL (a scripted
/// <c>FakeModelServer</c> on loopback, a staging proxy, or a real paid
/// upstream — whatever the human passed to <c>--upstream</c>), and redirects
/// real pi at the recording server's BaseAddress; every real-pi model call
/// is forwarded through to the upstream, redacted by
/// <c>Cassettes.Redaction.CassetteRedactor</c>, and appended to the cassette
/// at <c>CassettePath</c> by the existing
/// <c>Cassettes.Recording.CassetteRecordingState</c> write machinery. No
/// temp-file dance here — record-cassette.mjs's <c>--force</c> guard plus
/// a human's <c>git diff</c> review are the safety net.
/// </summary>
/// <remarks>
/// Sister to <see cref="CassetteGeneratingPiFakeModelHarness"/> but without
/// the hard-coded <c>FakeModelServer</c>-as-upstream constraint — that
/// harness exists to regenerate the one committed cassette
/// (<c>add-null-check.v1.json</c>) via a scripted fake. This one exists so
/// a maintainer can re-record any scenario's cassette against any upstream
/// the <c>--upstream</c> flag names (a paid API, an internal eval proxy, a
/// scratch fake server spun up just for the run), without copying the
/// harness.
/// </remarks>
/// <inheritdoc />
public sealed class RecordingPiFakeModelHarness(
    RealPiInstallation realPi,
    RealPiFakeModelHost host,
    string cassettePath,
    Uri upstreamBaseUrl,
    string recordedAgainst,
    BudgetTracker? budgetTracker = null) : RealPiHarnessBase(realPi, host)
{
    private CassetteModelServer? cassetteServer;

    /// <inheritdoc />
    protected override async Task<Uri> StartModelServerAsync(ScenarioDefinition scenario, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(cassettePath))
        {
            throw new InvalidOperationException(
                $"record-cassette run for scenario '{scenario.Name}': cassette path is empty.");
        }

        cassetteServer = new CassetteModelServer(new CassetteModelServerOptions
        {
            Mode = CassetteModelMode.Record,
            CassettePath = cassettePath,
            Scenario = scenario.Name,
            RecordedAgainst = recordedAgainst,
            UpstreamBaseUrl = upstreamBaseUrl,
            BudgetTracker = budgetTracker,
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

    /// <inheritdoc />
    public Task<RunCost> ReadCostAsync(Guid workItemId, CancellationToken cancellationToken = default)
    {
        return budgetTracker is { } tracker
            ? Task.FromResult(new RunCost
            {
                UsdMicros = tracker.UsdMicros,
                TokensIn = tracker.TokensIn,
                TokensOut = tracker.TokensOut,
            })
            : Task.FromResult(new RunCost());
    }
}
