namespace Comuki.TestFakeModel.Cassettes.Hosting;

/// <summary>Construction options for <see cref="CassetteModelServer"/>.</summary>
public sealed class CassetteModelServerOptions
{
    /// <summary>Which mode to run — <see cref="CassetteModelMode.Replay"/> or <see cref="CassetteModelMode.Record"/>.</summary>
    public required CassetteModelMode Mode { get; init; }

    /// <summary>
    /// The cassette file path. <see cref="CassetteModelMode.Replay"/> reads
    /// it (must exist). <see cref="CassetteModelMode.Record"/> appends to
    /// it, creating it (with <see cref="Scenario"/>/<see cref="RecordedAgainst"/>
    /// as its header) if it doesn't exist yet.
    /// </summary>
    public required string CassettePath { get; init; }

    /// <summary>New-cassette header field (record mode only) — ignored when the cassette already exists, since its own header wins.</summary>
    public string Scenario { get; init; } = "fake";

    /// <summary>New-cassette header field (record mode only) — the model id recorded against, e.g. <c>claude-sonnet-5</c>.</summary>
    public string RecordedAgainst { get; init; } = "unknown";

    /// <summary>Required when <see cref="Mode"/> is <see cref="CassetteModelMode.Record"/> — where <c>Recording.CassetteUpstreamForwarder</c> forwards each request.</summary>
    public Uri? UpstreamBaseUrl { get; init; }

    /// <summary>Loopback port to bind. <c>null</c> asks the OS for an ephemeral free port — see <c>Hosting.FakeModelServerOptions.Port</c>.</summary>
    public int? Port { get; init; }

    /// <summary>Address Kestrel binds — see <c>Hosting.FakeModelServerOptions.BindAddress</c>.</summary>
    public string BindAddress { get; init; } = "127.0.0.1";

    /// <summary>The clock <see cref="CassetteModelMode.Record"/> stamps each newly-appended exchange's cassette header with.</summary>
    public TimeProvider Clock { get; init; } = TimeProvider.System;

    /// <summary>
    /// Optional <see cref="BudgetTracker"/> the recording path checks before
    /// forwarding each upstream request. WS9 only — every existing caller
    /// (WS5/WS7/WS8) leaves this null, so behaviour is unchanged. When set
    /// and the tracker is already over budget, the recording path returns
    /// a typed <c>Anthropic.Errors.AnthropicErrors.ScriptFailure</c>
    /// <c>api_error</c> 500 to the caller and does NOT append that refused
    /// attempt to the cassette.
    /// </summary>
    public BudgetTracker? BudgetTracker { get; init; }

    /// <summary>
    /// USD per million input tokens — local placeholder for live-mode cost
    /// estimation (design.md's "defense in depth" alongside the target
    /// project's own budget gate). Deliberately <em>not</em> imported from
    /// <c>Comuki.Modules.Proxy.Application.Metering.ProxyPricingCalculator</c>
    /// to keep this test tool dependency-free; see the live harness's own
    /// doc comment for why. Default mirrors that production calculator's
    /// own <c>PricingTier</c> default so the WS9 acceptance numbers line up.
    /// </summary>
    public decimal UsdPerMillionInputTokens { get; init; } = 3m;

    /// <summary>USD per million output tokens — paired placeholder with <see cref="UsdPerMillionInputTokens"/>.</summary>
    public decimal UsdPerMillionOutputTokens { get; init; } = 15m;
}

/// <summary>
/// Tracker-side bookkeeping for live-mode cost enforcement — local to
/// <c>Comuki.TestFakeModel</c> so the recording server stays
/// dependency-free (does NOT reference
/// <c>Comuki.AgentTest.Runner.Execution.Budget.BudgetTracker</c>).
/// Identical semantics: accumulates <c>RunCost</c>s and exposes
/// <see cref="IsOverBudget"/>. The runner's own
/// <c>Comuki.AgentTest.Runner.Execution.Budget.BudgetTracker</c> holds the
/// post-run assertion side; this local copy sits in front of the forward
/// path so it can refuse to call the real upstream at all.
/// </summary>
/// <remarks>
/// Why duplicate the type rather than import the runner's: the runner is
/// a downstream tool of the test fixture, not the other way around —
/// adding a ProjectReference from the test's recording server to a test
/// tool's runner would be a layering inversion. The two trackers stay in
/// lock-step via the parent's own <c>BudgetCap.Resolve</c> +
/// <see cref="BudgetTracker"/> outside this project — the WS9 live harness
/// composes both: it owns a runner-side tracker for the post-run check
/// and a local tracker for the pre-forward refusal.
/// </remarks>
/// <remarks>Creates a tracker for <paramref name="cap"/>.</remarks>
/// <param name="cap">The resolved effective cap. Pass <see cref="BudgetCap.Unlimited"/> to disable enforcement.</param>
public sealed class BudgetTracker(BudgetCap cap)
{

    /// <summary>The cap this tracker enforces.</summary>
    public BudgetCap Cap { get; } = cap;

    /// <summary>The accumulated spend as of the last <see cref="Add"/> call (zero until then).</summary>
    public long UsdMicros { get; private set; }

    /// <summary>The accumulated input-token count across <see cref="Add"/> calls (zero until then).</summary>
    public long TokensIn { get; private set; }

    /// <summary>The accumulated output-token count across <see cref="Add"/> calls (zero until then).</summary>
    public long TokensOut { get; private set; }

    /// <summary>True once <see cref="UsdMicros"/> strictly exceeds the cap — never flips when the cap is unlimited.</summary>
    public bool IsOverBudget => Cap.IsOverBudget(UsdMicros);

    /// <summary>Records one exchange's contribution: USD-micro plus input/output token counts; both clamped to non-negative.</summary>
    /// <param name="usdMicrosDelta">The just-computed micro-USD cost of one exchange.</param>
    /// <param name="inputTokensDelta">Input tokens for the same exchange.</param>
    /// <param name="outputTokensDelta">Output tokens for the same exchange.</param>
    public void Add(long usdMicrosDelta, int inputTokensDelta, int outputTokensDelta)
    {
        UsdMicros += usdMicrosDelta < 0 ? 0 : usdMicrosDelta;
        TokensIn += inputTokensDelta < 0 ? 0 : inputTokensDelta;
        TokensOut += outputTokensDelta < 0 ? 0 : outputTokensDelta;
    }
}

/// <summary>
/// Resolved per-run USD cap — local, host-free, stand-alone — parallels
/// the runner-side <c>Comuki.AgentTest.Runner.Execution.Budget.BudgetCap</c>
/// with the same semantics so the recording path can reject requests in
/// lock-step with the runner's post-run assertion. Independent of any
/// IConfiguration / DI wiring.
/// </summary>
public readonly record struct BudgetCap
{
    private readonly long? usdMicros;

    /// <summary>
    /// Public factory for the bounded form. The <c>null</c> overload
    /// (<see cref="Unlimited"/>) is the other valid shape — use
    /// that for "no cap in force" so the tracker short-circuits
    /// <see cref="IsOverBudget"/> to false.
    /// </summary>
    /// <param name="usdMicros">A finite micro-USD cap; null = unlimited (use the static field).</param>
    public BudgetCap(long? usdMicros)
    {
        this.usdMicros = usdMicros;
    }

    /// <summary>The effective cap in micro-USD; null means "no cap in force".</summary>
    public long? UsdMicros => usdMicros;

    /// <summary>The "no cap is in force" sentinel.</summary>
    public static BudgetCap Unlimited { get; } = new(usdMicros: null);

    /// <summary>True iff the cap is set and <paramref name="observedUsdMicros"/> strictly exceeds it.</summary>
    public bool IsOverBudget(long observedUsdMicros)
    {
        return usdMicros is { } capMicros && observedUsdMicros > capMicros;
    }

    /// <summary>True iff a finite micro-USD bound is in force.</summary>
    public bool IsBounded => usdMicros is not null;
}

