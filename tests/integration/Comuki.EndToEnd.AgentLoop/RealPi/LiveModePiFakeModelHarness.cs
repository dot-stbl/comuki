using Comuki.AgentTest.Runner.Execution;
using Comuki.AgentTest.Runner.Reporting.Report;
using Comuki.AgentTest.Runner.Scenarios;
using Comuki.TestFakeModel.Cassettes.Hosting;
// Local alias — the runner-side cap/tracker types live in this project too, but
// the integration test's primary contract is the runner-side cap (whose semantics
// the scenario + assertions.cost checks already trust); the TestFakeModel-local
// duplicate (used by the recording endpoint) is fully qualified where needed.
using BudgetCap = Comuki.AgentTest.Runner.Execution.Budget.BudgetCap;
using CassetteBudgetCap = Comuki.TestFakeModel.Cassettes.Hosting.BudgetCap;
using CassetteBudgetTracker = Comuki.TestFakeModel.Cassettes.Hosting.BudgetTracker;

namespace Comuki.EndToEnd.AgentLoop.RealPi;

/// <summary>
/// WS9's <c>model.mode: live</c> <see cref="IAgentLoopHarness"/>:
/// real <c>pi</c> redirected at a recording <see cref="CassetteModelServer"/>
/// pointing at a caller-supplied upstream URL (the hapy gateway in production,
/// a scripted <c>FakeModelServer</c> on loopback in CI). The recorder
/// enforces a hard per-run <c>BudgetCap</c> cap so a runaway live
/// run is rejected at the next inbound POST (no external polling point
/// exists in a single-blocking-call harness — see the recorder endpoint's
/// own doc comment on the design call).
/// </summary>
/// <remarks>
/// <para>
/// Token-to-USD conversion lives here in two places on purpose (defense
/// in depth per design.md D5):
/// <list type="bullet">
///   <item>
///     <see cref="ReadCostAsync"/> reads the per-run
///     <c>BudgetTracker</c> the
///     <c>Recording.CassetteRecordingState</c> maintainer has accumulated
///     across this run's exchanges — that becomes the runner-side
///     <c>ScenarioResult.Cost</c> for the runner's <c>assertions.cost</c> /
///     <c>budget</c> post-run checks.
///   </item>
///   <item>
///     The same tracker the recorder checks on every forward is what this
///     harness constructs and passes in — a single instance, the same
///     instance the recorder is already holding a reference to.
///   </item>
/// </list>
/// </para>
/// <para>
/// Pricing defaults (<see cref="CassetteModelServerOptions.UsdPerMillionInputTokens"/>,
/// <see cref="CassetteModelServerOptions.UsdPerMillionOutputTokens"/>) match
/// <c>Comuki.Modules.Proxy.Application.Options.ProxyOptions.PricingTier</c>'s
/// own <c>(3, 15)</c> defaults. Deliberately <em>not</em> imported from
/// <c>Comuki.Modules.Proxy.Application.Metering.ProxyPricingCalculator</c>
/// — this test-tool must stay dependency-free of the orchestrator's app
/// modules (test-tool composition root = agent-loop collection; adding a
/// ProjectReference to the orchestrator's app modules would invert the
/// layering the existing test tool project tree enforces).
/// </para>
/// </remarks>
public sealed class LiveModePiFakeModelHarness(
    RealPiInstallation realPi,
    RealPiFakeModelHost host,
    BudgetCap budgetCap,
    string cassettePath) : RealPiHarnessBase(realPi, host)
{
    /// <summary>Env var the recording path consults for the API token; defaults to a placeholder string when unset.</summary>
    public const string LiveTokenEnvVar = "COMUKI_LIVE_MODEL_TOKEN";

    /// <summary>Env var the recording path consults for the API base URL (the hapy gateway, an upstream we trust).</summary>
    public const string LiveBaseUrlEnvVar = "COMUKI_LIVE_MODEL_BASE_URL";

    /// <summary>The placeholder token stamped when no <see cref="LiveTokenEnvVar"/> env value is present. Same shape <see cref="RealPiHarnessBase"/> uses for its own fake-token stamp.</summary>
    private const string PlaceholderToken = "real-pi-live-mode-test-token";

    private CassetteModelServer? cassetteServer;

    /// <summary>The recorder-side tracker the cassette server hands every forwarded request through. Lives the lifetime of one <see cref="StartModelServerAsync"/>. The TestFakeModel-local <c>BudgetTracker</c>, NOT the runner-side one — the recorder only knows the local shape.</summary>
    private CassetteBudgetTracker? tracker;

    /// <summary>The cost the runner reads post-run — the same tracker's accumulated micro-USD at disposal time.</summary>
    private long observedUsdMicros;
    private long observedTokensIn;
    private long observedTokensOut;

    /// <inheritdoc />
    protected override async Task<Uri> StartModelServerAsync(ScenarioDefinition scenario, CancellationToken cancellationToken)
    {
        if (scenario.Model is not { Mode: ScenarioModelMode.Live })
        {
            throw new InvalidOperationException(
                $"scenario '{scenario.Name}' does not declare model.mode: live — LiveModePiFakeModelHarness has no live upstream to point at.");
        }

        var upstreamRaw = Environment.GetEnvironmentVariable(LiveBaseUrlEnvVar);
        if (string.IsNullOrWhiteSpace(upstreamRaw))
        {
            throw new InvalidOperationException(
                $"{LiveBaseUrlEnvVar} is not set — LiveModePiFakeModelHarness refuses to pick an upstream URL itself.");
        }

        if (!Uri.TryCreate(upstreamRaw, UriKind.Absolute, out var upstreamBaseUrl))
        {
            throw new InvalidOperationException(
                $"{LiveBaseUrlEnvVar}='{upstreamRaw}' is not a valid absolute URL");
        }

        // Translate the runner-side resolved cap into the TestFakeModel-local
        // cap shape before handing it to the recorder. The semantics are
        // identical (== IsOverBudget(IsOverBudget) on the matched value).
        tracker = new CassetteBudgetTracker(
            budgetCap.IsBounded
                ? new CassetteBudgetCap(budgetCap.UsdMicros ?? 0L)
                : CassetteBudgetCap.Unlimited);
        var options = new CassetteModelServerOptions
        {
            Mode = CassetteModelMode.Record,
            CassettePath = cassettePath,
            Scenario = scenario.Name,
            RecordedAgainst = $"recorded-live-upstream-{upstreamBaseUrl.Host}",
            UpstreamBaseUrl = upstreamBaseUrl,
            BudgetTracker = tracker,
        };

        // Stamp the ANTHROPIC_AUTH_TOKEN env var (same trick the other
        // harnesses use) to the live token if one is set, otherwise to a
        // placeholder. CassetteUpstreamForwarder forwards request headers
        // verbatim to the real upstream — only ANTHROPIC_AUTH_TOKEN removal
        // is a concern, but the bare-bones forwarder keeps headers as-is,
        // and redaction strips bearer-shaped strings on the way back out.
        var token = Environment.GetEnvironmentVariable(LiveTokenEnvVar) ?? PlaceholderToken;
        Environment.SetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN", token);

        cassetteServer = new CassetteModelServer(options);
        await cassetteServer.StartAsync(cancellationToken);

        return cassetteServer.BaseAddress;
    }

    /// <inheritdoc />
    public Task<RunCost> ReadCostAsync(Guid workItemId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new RunCost
        {
            UsdMicros = observedUsdMicros,
            TokensIn = observedTokensIn,
            TokensOut = observedTokensOut,
        });
    }

    /// <inheritdoc />
    protected override async ValueTask DisposeModelServerAsync()
    {
        if (cassetteServer is not null)
        {
            // Snapshot the tracker before disposing the server — once the
            // server's Recording state is gone (and with it, the gateway
            // through AnthropicUsageExtractor passed each forward), the
            // tracker is the only authoritative read.
            if (tracker is not null)
            {
                observedUsdMicros = tracker.UsdMicros;
                observedTokensIn = tracker.TokensIn;
                observedTokensOut = tracker.TokensOut;
            }

            await cassetteServer.DisposeAsync();
            cassetteServer = null;
            tracker = null;
        }
    }

    /// <summary>In-process helper for unit tests / fresh-harness probes — exposes the resolved cap so a test can introspect what cap the recording path will enforce.</summary>
    /// <param name="scenarioMaxUsd">Optional scenario-level cap (decimal USD).</param>
    /// <param name="globalBudgetUsdRaw">Optional override for the <c>COMUKI_LIVE_BUDGET_MAX_USD</c> env value (used by the WS9 runner-side cap; this harness reads the cap directly).</param>
    public static BudgetCap BuildCap(decimal? scenarioMaxUsd, string? globalBudgetUsdRaw = null)
    {
        return BudgetCap.Resolve(scenarioMaxUsd, globalBudgetUsdRaw);
    }
}
