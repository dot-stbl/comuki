using Comuki.Engine.Compute.Options;
using Comuki.Engine.Orchestration.Options;
using Comuki.Modules.Identity.Application.Permissions;
using Comuki.Modules.Proxy.Application.Options;
using Microsoft.Extensions.Options;

namespace Comuki.Host.Settings;

/// <summary>
/// Wire shape of <c>GET /api/v1/settings</c> — a read-only snapshot of the
/// platform-level settings that already exist as bound options. Every value
/// is <c>IOptions</c>-backed (fixed at startup); there is deliberately no
/// PUT: nothing in this surface is mutable at runtime, and pretending
/// otherwise would be phantom success. Changes go through configuration +
/// a restart.
/// </summary>
/// <param name="Orchestration">Queue / lease / escalation policy knobs.</param>
/// <param name="Compute">Compute provider selection + scale defaults.</param>
/// <param name="Proxy">Proxy passthrough on/off state.</param>
public sealed record SettingsView(
    OrchestrationSettingsView Orchestration,
    ComputeSettingsView Compute,
    ProxySettingsView Proxy);

/// <summary>Orchestration policy as bound from <c>Orchestration:*</c>.</summary>
/// <param name="Lease">Claim/lease policy.</param>
/// <param name="EscalationTimeout">Escalation ratchet policy (incl. the kill-switch).</param>
public sealed record OrchestrationSettingsView(
    LeaseSettingsView Lease,
    EscalationTimeoutSettingsView EscalationTimeout);

/// <summary>Claim/lease policy from <see cref="LeaseOptions"/>.</summary>
/// <param name="LeaseTtlSeconds">Lease duration handed out on claim / extended by heartbeat.</param>
/// <param name="ReapIntervalSeconds">Lease reaper sweep cadence.</param>
/// <param name="ReapGraceSeconds">Buffer past lease expiry before the reaper acts.</param>
/// <param name="MaxAttempts">Claim attempts before the reaper fails a stalled item.</param>
public sealed record LeaseSettingsView(
    long LeaseTtlSeconds,
    long ReapIntervalSeconds,
    long ReapGraceSeconds,
    int MaxAttempts);

/// <summary>Escalation ratchet from <see cref="EscalationTimeoutOptions"/>.</summary>
/// <param name="Enabled">Ops kill-switch — false disables the passive ratchet globally.</param>
/// <param name="TimeoutSeconds">How long a run may sit Escalated before auto-archival.</param>
/// <param name="SweepIntervalSeconds">Ratchet sweep cadence.</param>
public sealed record EscalationTimeoutSettingsView(
    bool Enabled,
    long TimeoutSeconds,
    long SweepIntervalSeconds);

/// <summary>Compute selection + scale defaults from <c>Compute:*</c>.</summary>
/// <param name="Provider">Active provider key: <c>docker</c> or <c>kubernetes</c>.</param>
/// <param name="Scale">Scale supervisor defaults.</param>
public sealed record ComputeSettingsView(
    string Provider,
    ComputeScaleSettingsView Scale);

/// <summary>Scale supervisor defaults from <see cref="ScaleSupervisorOptions"/>.</summary>
/// <param name="WorkerImage">Default worker image.</param>
/// <param name="ProfilesGitRef">Default pinned profiles git ref.</param>
/// <param name="MinIdle">Warm-idle floor per profile.</param>
/// <param name="MaxConcurrent">Concurrency cap per project.</param>
/// <param name="IdleTtlSeconds">Idle TTL before a worker is a reaper candidate.</param>
/// <param name="PollIntervalSeconds">Delay between supervisor passes.</param>
public sealed record ComputeScaleSettingsView(
    string WorkerImage,
    string ProfilesGitRef,
    int MinIdle,
    int MaxConcurrent,
    long IdleTtlSeconds,
    long PollIntervalSeconds);

/// <summary>Proxy state from <see cref="ProxyOptions"/>.</summary>
/// <param name="Enabled">Whether the OpenAI/Anthropic passthrough is composed.</param>
public sealed record ProxySettingsView(bool Enabled);

/// <summary>
/// Platform settings snapshot (permission <c>settings:read</c>): read-only
/// projection of the options the host binds — autonomy timeouts, lease
/// policy, scale defaults and the proxy switch.
/// </summary>
public static class SettingsEndpoints
{
    /// <summary>Maps the settings snapshot endpoint.</summary>
    /// <param name="app"></param>
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(ApiRoutes.Settings, GetSettingsAsync).WithTags("Settings");
        return app;
    }

    [RequiresPermission("settings:read")]
    private static IResult GetSettingsAsync(
        IOptions<LeaseOptions> lease,
        IOptions<EscalationTimeoutOptions> escalationTimeout,
        IOptions<ComputeOptions> compute,
        IOptions<ScaleSupervisorOptions> scale,
        IOptions<ProxyOptions> proxy)
    {
        var settings = new SettingsView(
            new OrchestrationSettingsView(
                new LeaseSettingsView(
                    (long)lease.Value.LeaseTtl.TotalSeconds,
                    (long)lease.Value.ReapInterval.TotalSeconds,
                    (long)lease.Value.ReapGrace.TotalSeconds,
                    lease.Value.MaxAttempts),
                new EscalationTimeoutSettingsView(
                    escalationTimeout.Value.Enabled,
                    (long)escalationTimeout.Value.EscalationTimeout.TotalSeconds,
                    (long)escalationTimeout.Value.SweepInterval.TotalSeconds)),
            new ComputeSettingsView(
                compute.Value.Provider,
                new ComputeScaleSettingsView(
                    scale.Value.WorkerImage,
                    scale.Value.ProfilesGitRef,
                    scale.Value.MinIdle,
                    scale.Value.MaxConcurrent,
                    (long)scale.Value.IdleTtl.TotalSeconds,
                    (long)scale.Value.PollInterval.TotalSeconds)),
            new ProxySettingsView(proxy.Value.Enabled));

        return Results.Ok(settings);
    }
}
