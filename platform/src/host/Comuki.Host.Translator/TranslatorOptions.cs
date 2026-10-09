using System.ComponentModel.DataAnnotations;

namespace Comuki.Host.Translator;

/// <summary>
/// Everything the worker container needs to run: where the orchestrator
/// lives (REST + gRPC), the worker token (from <c>COMUKI_WORKER_TOKEN</c>),
/// the claim labels (image / profiles ref / profile key from the
/// <c>COMUKI_*</c> environment the compute provider stamped), and the pi
/// executable to spawn. Bound from the <c>Translator</c> config section;
/// <see cref="Program"/> maps the <c>COMUKI_*</c> env onto it.
/// </summary>
public sealed class TranslatorOptions
{
    /// <summary>Config section: <c>Translator</c>.</summary>
    public const string SectionName = "Translator";

    /// <summary>Base URL of the orchestrator REST API (claim/heartbeat/complete/fail).</summary>
    [Required]
    public required Uri OrchestratorBaseUrl { get; init; }

    /// <summary>URL of the orchestrator gRPC endpoint (worker bidi stream).</summary>
    [Required]
    public required Uri OrchestratorGrpcUrl { get; init; }

    /// <summary>Opaque worker token; validated by the orchestrator on every call.</summary>
    [Required]
    [MinLength(16)]
    public required string WorkerToken { get; init; }

    /// <summary>Profile key this worker was scaled for (claim label).</summary>
    [Required]
    public required string ProfileKey { get; init; }

    /// <summary>Pinned profiles git ref this worker runs (claim label).</summary>
    [Required]
    public required string ProfilesRef { get; init; }

    /// <summary>Worker image digest (claim label — mirrors the container image).</summary>
    [Required]
    public required string WorkerImage { get; init; }

    /// <summary>
    /// Environment class the worker was scaled for (claim label).
    /// Sourced from <c>COMUKI_ENV_CLASS</c>; the compute provider stamps
    /// it on the container at Start. Required once this change ships as
    /// the running contract — task 3.2 wires the worker claim body to
    /// demand it. Default-empty for this slice: the orchestrator accepts
    /// an empty value as "no class bound" and the queue's
    /// <c>env_class = @envClass</c> filter guarantees no claimer ever
    /// matches such an item, so the worker simply sees 204 on every claim.
    /// A separate compute-side change (WS3.3) flips this to required.
    /// </summary>
    public string EnvClass { get; init; } = string.Empty;

    /// <summary>Executable spawned per work item. Production: <c>pi</c>; tests: TestFakePi.</summary>
    public string PiExecutable { get; init; } = "pi";

    /// <summary>Working directory for the spawned process (the mounted worktree).</summary>
    public string WorkingDirectory { get; init; } = Directory.GetCurrentDirectory();

    /// <summary>Local mounted directory with client profiles; unset skips the copy with a warning.</summary>
    public string? ProfilesPath { get; init; }

    /// <summary>Public git URL with client profiles; used when <see cref="ProfilesPath"/> is unset.</summary>
    public Uri? ProfilesGitUrl { get; init; }

    /// <summary>How long to wait between claim attempts when the queue has nothing.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:10:00")]
    public TimeSpan ClaimPollInterval { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>How often to extend the lease while an item is running.</summary>
    [Range(typeof(TimeSpan), "00:00:05", "01:00:00")]
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Opt-in operator debug (harden-pi-worker-sandbox 5.3, spec
    /// "Operator debug is opt-in"). When true, the worker accepts
    /// <c>Exec</c> commands delivered over the worker gRPC stream and
    /// spawns the requested child process inside its own boundary.
    /// Default off: production workers MUST refuse Exec when this is
    /// false, the refusal is logged and never executed. Bound from
    /// <c>Translator:DebugExec</c>; the compute provider stamps the
    /// value through <c>COMUKI_DEBUG_EXEC</c> on the worker boundary
    /// when they want a one-off debuggable slot. Operators switch it on
    /// deliberately for one run; off is the safe default for
    /// everything else.
    /// </summary>
    public bool DebugExec { get; init; }

    /// <summary>
    /// Progress-watchdog threshold (harden-worker-runtime Phase 1, design D1).
    /// Tracks <c>last_event_age</c> — the time since the last parsed
    /// stream-event (text delta, tool_use, tool_result, StageStart,
    /// StageReport, agent_end, system, user, message_end,
    /// tool_execution_start). Heartbeat is the *liveness* timer; this
    /// is the *progress* timer — heartbeat without progress = stall.
    /// Default 60s; range 5s–1h. The escalation path is policy-driven
    /// (see <see cref="WorkerProgressEscalationPolicy"/>).
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:05", "01:00:00")]
    public TimeSpan WorkerProgressTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Escalation policy the <c>WorkerProgressWatchdog</c> walks
    /// through when <c>last_event_age &gt; WorkerProgressTimeout</c>
    /// (harden-worker-runtime Phase 1, design D1):
    /// <list type="bullet">
    ///   <item><c>Warn</c> — log + journal <c>worker.stall_warn</c>; no action.</item>
    ///   <item><c>GentleKill</c> — cancel the harness's <c>RunCancellation</c> token; the pump
    ///   exits with <c>cancelled</c>, the loop skips complete and lets the
    ///   reaper own the item.</item>
    ///   <item><c>FailItem</c> — cancel + <c>api.FailAsync(stall_detected)</c>; the run
    ///   reports a typed reason and the host can re-queue.</item>
    /// </list>
    /// Default <c>WarnGentleKillFailItem</c>: the full escalation chain
    /// fires at <c>WorkerProgressTimeout</c> intervals (warn → gentle-kill
    /// → fail-item).
    /// </summary>
    public WorkerProgressEscalationPolicy WorkerProgressEscalationPolicy { get; init; } =
        WorkerProgressEscalationPolicy.WarnGentleKillFailItem;

    /// <summary>
    /// Wall-clock budget on a single harness cycle (one spawn → one
    /// StageReport; harden-worker-runtime Phase 1, design D2). On
    /// breach, escalation path is the same as the progress watchdog
    /// (gentle-kill on first breach; after
    /// <see cref="ConsecutiveTurnBreachesBeforeFail"/> consecutive
    /// breaches inside the same run, the item is failed). Default
    /// 60min; range 5min–8h.
    /// </summary>
    [Range(typeof(TimeSpan), "00:05:00", "08:00:00")]
    public TimeSpan TurnBudget { get; init; } = TimeSpan.FromMinutes(60);

    /// <summary>
    /// Wall-clock budget on a single worker process lifetime (one or
    /// more cycles; harden-worker-runtime Phase 1, design D2). On
    /// breach, fail-item with reason <c>worker.run_budget_exceeded</c>
    /// and let the host re-queue. Default 480min (8h); range
    /// 15min–24h.
    /// </summary>
    [Range(typeof(TimeSpan), "00:15:00", "1.00:00:00")]
    public TimeSpan RunBudget { get; init; } = TimeSpan.FromHours(8);

    /// <summary>
    /// Number of consecutive turn-budget breaches inside one worker
    /// process before the item is failed (harden-worker-runtime
    /// Phase 1, design D2). 1 turn-budget breach = gentle-kill
    /// (idempotent restart); 3 breaches in a row = fail-item.
    /// Default 3; range 1–10.
    /// </summary>
    [Range(1, 10)]
    public int ConsecutiveTurnBreachesBeforeFail { get; init; } = 3;

    /// <summary>
    /// Per-line cap on the pi stream-json reader
    /// (harden-worker-runtime Phase 3, design D4). Lines longer than
    /// this are dropped (the rest of the line is consumed from the
    /// stream so the next call sees the start of the next line),
    /// the reader invokes the pump's
    /// <see cref="Runtime.WorkerEventsChannel.OnProgressDropped"/>
    /// callback (which journals <c>worker.events_dropped</c>), and
    /// the worker keeps reading — one bad line cannot OOM the
    /// process. Default 1 MB; <c>0</c> disables the cap
    /// (non-production only — the test fake harness sets 0).
    /// </summary>
    [Range(0, 64 * 1024 * 1024)]
    public int MaxLineLengthBytes { get; init; } = 1 * 1024 * 1024;

    /// <summary>
    /// Bounded capacity of the harness events channel
    /// (harden-worker-runtime Phase 3, design D4). The channel drops
    /// progress-fragments (<c>text_delta</c>) on drop-oldest;
    /// <c>agent_end</c> (the only mandatory <c>PiEvent</c> on the
    /// stream-json side) waits for the consumer instead. The
    /// run-level lifecycle events (<c>StageStart</c>,
    /// <c>StageReport</c>) are surfaced over the gRPC stream by
    /// the loop, not through this channel. Default 1024; range
    /// 16–16384.
    /// </summary>
    [Range(16, 16384)]
    public int EventsChannelCapacity { get; init; } = 1024;
}

/// <summary>
/// Tiered escalation the <c>WorkerProgressWatchdog</c> walks
/// through (harden-worker-runtime Phase 1, design D1). The flag
/// encodes the highest tier that fires: Warn-only stops at the
/// journal; GentleKill cancels the harness on the second tick;
/// FailItem (Warn + GentleKill + FailItem) sets
/// <c>ShouldFailItem</c> + <c>FailReason = "worker.stall_detected"</c>
/// on the third; the pump reads both and the loop's existing
/// <c>api.FailAsync</c> call (in <c>TranslatorLoop</c>) handles
/// the REST side with the typed reason.
/// </summary>
[Flags]
public enum WorkerProgressEscalationPolicy
{
    /// <summary>No escalation; the watchdog is a passive gauge.</summary>
    None = 0,

    /// <summary>Tier 1 — journal <c>worker.stall_warn</c> on the first tick.</summary>
    Warn = 1,

    /// <summary>Tier 2 — cancel the harness on the second tick.</summary>
    GentleKill = 2,

    /// <summary>Tier 3 — set <c>ShouldFailItem = true</c> +
    /// <c>FailReason = "worker.stall_detected"</c> on the third tick;
    /// the loop's <c>api.FailAsync</c> call (in <c>TranslatorLoop</c>)
    /// handles the REST side with the typed reason.</summary>
    FailItem = 4,

    /// <summary>Default chain — warn, then gentle-kill, then fail-item.</summary>
    WarnGentleKillFailItem = Warn | GentleKill | FailItem,
}
