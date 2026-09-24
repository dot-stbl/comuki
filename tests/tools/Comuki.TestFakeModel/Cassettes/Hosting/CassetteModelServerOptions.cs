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
