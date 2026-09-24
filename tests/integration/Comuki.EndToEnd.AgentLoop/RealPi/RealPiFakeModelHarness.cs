using Comuki.AgentTest.Runner.Execution;
using Comuki.AgentTest.Runner.Scenarios;
using Comuki.TestFakeModel;
using Comuki.TestFakeModel.Hosting;
using Comuki.TestFakeModel.Scripting.Loading;

namespace Comuki.EndToEnd.AgentLoop.RealPi;

/// <summary>
/// T2b's <see cref="IAgentLoopHarness"/> (design.md D3 / WS7 task 7.1):
/// seeds through <see cref="RealPiFakeModelHost"/>'s real webhook exactly
/// like T2a's <see cref="AgentLoopHarness"/> does, but "starts the worker"
/// by running the real <c>TranslatorLoop</c> in-process — spawning the
/// real, vendored <c>pi</c> binary (<see cref="RealPiInstallation"/>)
/// pointed at a scenario-scripted <see cref="FakeModelServer"/> instead of
/// provisioning a container. No Docker/Podman dependency anywhere in this
/// class — see the class remarks on <see cref="RealPiFakeModelHost"/> for
/// why that trade-off was made deliberately.
/// </summary>
/// <remarks>
/// Issue #150 (this is the test that proves/needs it): pi's built-in model
/// catalog hardcodes a per-model <c>baseUrl</c> for every cataloged
/// Anthropic model (verified against the vendored 0.85.1 bundle — grepping
/// its shipped JS for <c>ANTHROPIC_BASE_URL</c> finds zero references at
/// all); only a <c>providers.anthropic.baseUrl</c> entry in
/// <c>models.json</c> under <c>PI_CODING_AGENT_DIR</c> redirects it
/// (verified with a real pi process against a throwaway stub server).
/// Comuki's production Translator (<c>PiEnvironment.FromClaim</c>) stamps
/// only <c>ANTHROPIC_BASE_URL</c>/<c>ANTHROPIC_AUTH_TOKEN</c> today — no
/// <c>models.json</c> is written anywhere in <c>platform/src</c>. This
/// harness therefore does NOT rely on that stamp to redirect pi (it would
/// silently no-op, exactly demonstrating the bug): it independently sets
/// <c>PI_CODING_AGENT_DIR</c>/<c>ANTHROPIC_AUTH_TOKEN</c> on this TEST
/// process's own environment before running the translator loop, standing
/// in for the still-missing production fix — see the WS7 report for the
/// exact production change this proves is needed.
/// </remarks>
public sealed class RealPiFakeModelHarness : RealPiHarnessBase
{
    private FakeModelServer? fakeModelServer;

    /// <summary>The fake model's own observed-request log for the one scenario this harness ran — asserted against directly by the test (WS7's "prove requests reach the fake model").</summary>
    public IReadOnlyList<RecordedRequest> FakeModelRequests => fakeModelServer?.Requests ?? [];

    /// <inheritdoc />
    public RealPiFakeModelHarness(RealPiInstallation realPi, RealPiFakeModelHost host)
        : base(realPi, host)
    {
    }

    /// <inheritdoc />
    protected override async Task<Uri> StartModelServerAsync(ScenarioDefinition scenario, CancellationToken cancellationToken)
    {
        if (scenario.Model is not { FakeScript: { Length: > 0 } fakeScriptRelativePath })
        {
            throw new InvalidOperationException(
                $"scenario '{scenario.Name}' declares no model.fakeScript — RealPiFakeModelHarness has nothing to script the fake model with.");
        }

        var fakeScriptPath = ScenarioLoader.ResolveRelativeToScenario(scenario.SourcePath, fakeScriptRelativePath);
        var script = FakeScriptLoader.LoadFromFile(fakeScriptPath, scenario.Name);
        fakeModelServer = new FakeModelServer(new FakeModelServerOptions { Script = script });
        await fakeModelServer.StartAsync(cancellationToken);

        return fakeModelServer.BaseAddress;
    }

    /// <inheritdoc />
    protected override async ValueTask DisposeModelServerAsync()
    {
        if (fakeModelServer is not null)
        {
            await fakeModelServer.DisposeAsync();
            fakeModelServer = null;
        }
    }
}
