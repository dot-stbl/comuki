namespace Comuki.Modules.Work.Infrastructure.Options;

/// <summary>
/// Work module feature flags + dispatch-policy defaults — bound
/// from the <c>[work]</c> TOML section (the post-#88 unprefixed
/// <c>work</c> schema convention per
/// <c>add-work-management/design.md</c> Open Question C). Every
/// flag has a default that keeps prod behaviour opt-in; the
/// <c>admission.enabled</c> gate is the umbrella's Phase A
/// boundary — the Work inbox subscriber reads from
/// <c>integration.inbound.admitted.v1</c> only when the flag is
/// on.
/// </summary>
public sealed class WorkOptions
{
    /// <summary>The configuration section name — bound in <c>HostComposer</c>.</summary>
    public const string SectionName = "Work";

    /// <summary>
    /// When <c>true</c> the Integrations-admission hook
    /// (<c>WorkAdmissionSubscriber</c>) reads
    /// <c>integration.inbound.admitted.v1</c> and dispatches
    /// <c>Work.AdmitTask</c>. Off by default in prod until Phase A
    /// ships; the gate is the umbrella's "standalone Tasks"
    /// cutover marker.
    /// </summary>
    public bool AdmissionEnabled { get; init; }

    /// <summary>
    /// When <c>true</c> the Work→Integrations sync-bridge
    /// (<c>WorkSyncBridgeComukiWorker</c>) reads
    /// <c>work.task.resolved.v1</c> / <c>work.task.cancelled.v1</c>
    /// and emits one deduped outbound sync job per Task id.
    /// Phase C gate — replaces the legacy
    /// <c>RunStatusBridgeComukiWorker</c> when on.
    /// </summary>
    public bool SyncEnabled { get; init; }

    /// <summary>
    /// When <c>true</c> the <c>WorkDispatchRequestedSubscriber</c>
    /// launches runs through <c>IRunLauncher.LaunchAsync</c>.
    /// Off by default until the dispatch seam lands (see design
    /// §Decision 2).
    /// </summary>
    public bool DispatchEnabled { get; init; }

    /// <summary>
    /// When <c>true</c> the <c>WorkAttemptCancelledSubscriber</c>
    /// forwards <c>orchestration.run.cancelled.v1</c> events
    /// into <c>HostCancelRunAdapter</c>. Off by default until the
    /// engine-side emission lands (Hidden F).
    /// </summary>
    public bool CancelForwardingEnabled { get; init; }

    /// <summary>Dispatch policy the worker profile the dispatch subscriber selects (e.g. <c>implement</c>, <c>pr-review</c>).</summary>
    public string DefaultProfileKey { get; init; } = "implement";

    /// <summary>Worker image (with digest) the dispatch subscriber claims by default.</summary>
    public string DefaultWorkerImage { get; init; } = "ghcr.io/comuki/worker@sha256:0000000000000000000000000000000000000000000000000000000000000000";

    /// <summary>Environment class the dispatch subscriber claims by default.</summary>
    public string DefaultEnvClass { get; init; } = "default";

    /// <summary>Pinned git ref of the profiles repo the dispatch subscriber claims by default.</summary>
    public string DefaultProfilesRef { get; init; } = "refs/heads/main";
}
