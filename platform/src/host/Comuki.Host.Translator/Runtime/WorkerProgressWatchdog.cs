using Comuki.Host.Translator.Execution.Loop;
using Comuki.Host.Translator.Execution.Run;
using Microsoft.Extensions.Options;

namespace Comuki.Host.Translator.Runtime;

/// <summary>
/// Progress watchdog (harden-worker-runtime Phase 1, design D1). Tracks
/// <c>last_event_age</c> — the time since the last parsed stream-event
/// (text delta, tool_use, tool_result, StageStart, StageReport,
/// agent_end, system, user, message_end, tool_execution_start) — and
/// escalates through three tiers when the silence exceeds
/// <see cref="TranslatorOptions.WorkerProgressTimeout"/>:
/// <list type="bullet">
///   <item>tier 1 (warn) — log + journal <c>worker.stall_warn</c> over
///   the gRPC stream; the harness is still alive, the lease is still
///   held. Pure visibility.</item>
///   <item>tier 2 (gentle-kill) — cancel
///   <see cref="WorkerRun.RunCancellation"/>. The pump exits with
///   <c>cancelled</c>, the loop skips complete and lets the reaper own
///   the item.</item>
///   <item>tier 3 (fail-item) — cancel the run AND call
///   <c>api.FailAsync(stall_detected)</c>; the run reports a typed
///   reason and the host can re-queue. The pump reads
///   <see cref="ShouldFailItem"/> after the iteration and exits without
///   forwarding the result.</item>
/// </list>
/// Heartbeat is the *liveness* timer (REST <c>POST /workers/{id}/heartbeat</c>);
/// this watchdog is the *progress* timer — heartbeat without progress
/// is a stall. The two coexist: heartbeat continues to be exercised
/// (the orchestrator's lease-side check); the watchdog surfaces the
/// progress-stall.
/// </summary>
/// <remarks>
/// State is reset by the pump on every parsed event via
/// <see cref="Reset"/>. The watchdog itself owns the timer
/// (<see cref="Timer"/>); the pump owns the
/// lifetime — create, <see cref="Start"/>, <see cref="Stop"/>.
/// </remarks>
public sealed class WorkerProgressWatchdog : IDisposable
{
    private readonly WorkerRun run;
    private readonly TranslatorOptions options;
    private readonly TimeProvider clock;
    private readonly ILogger<WorkerProgressWatchdog> logger;
    private readonly TimeSpan tickInterval;
    private readonly Timer timer;
    private readonly Lock gate = new();
    private DateTimeOffset lastEventAt;
    private int currentTier;
    private bool disposed;

    /// <summary>
    /// Constructs the watchdog. The constructor does NOT start the
    /// timer; the pump drives that via <see cref="Start"/> so the
    /// initial <c>lastEventAt</c> is the cycle's spawn instant rather
    /// than the watchdog-construction instant.
    /// </summary>
    /// <param name="run">The bound run — the watchdog cancels its
    /// <see cref="WorkerRun.RunCancellation"/> on tier 2 and reads
    /// <see cref="WorkerRun.Claimed"/> for the journal payload.</param>
    /// <param name="options">Bound <c>Translator</c> options — carries
    /// <c>WorkerProgressTimeout</c> and the escalation policy.</param>
    /// <param name="clock">Injected for tests; the watchdog never reads
    /// <c>DateTimeOffset.UtcNow</c> directly.</param>
    /// <param name="logger">Records the tier transitions; tier 1 is
    /// warning (operator visibility), tier 2 is warning (gentle-kill
    /// fired), tier 3 is error (item failed).</param>
    public WorkerProgressWatchdog(
        WorkerRun run,
        IOptions<TranslatorOptions> options,
        TimeProvider clock,
        ILogger<WorkerProgressWatchdog> logger)
    {
        this.run = run;
        this.options = options.Value;
        this.clock = clock;
        this.logger = logger;
        // The timer fires every (WorkerProgressTimeout / 6) — 10s for
        // the 60s default. Caps at 30s (a 5min timeout still ticks
        // every 30s; a 1h timeout ticks every 30s instead of 10min)
        // and floors at 100ms (a 60s default still ticks every 10s;
        // a 1s test timeout ticks every 166ms). The floor is small
        // enough to keep unit tests responsive (the timer is real-
        // time-driven, not TimeProvider-driven) and large enough to
        // keep the timer from hammering the system on small budgets.
        var interval = TimeSpan.FromMilliseconds(
            Math.Clamp(this.options.WorkerProgressTimeout.TotalMilliseconds / 6, 100, 30_000));
        tickInterval = interval;
        lastEventAt = clock.GetUtcNow();
        timer = new Timer(OnTick, state: null, dueTime: Timeout.Infinite, period: Timeout.Infinite);
    }

    /// <summary>Arms the timer; the pump calls this once per cycle.</summary>
    public void Start()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            lastEventAt = clock.GetUtcNow();
            currentTier = 0;
            ShouldFailItem = false;
            timer.Change(tickInterval, tickInterval);
        }
    }

    /// <summary>Disarms the timer without disposing; the pump calls this when the cycle ends.</summary>
    public void Stop()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            timer.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    /// <summary>
    /// Resets the watchdog on every parsed event. The pump calls this
    /// from the events iterator. Tier returns to zero (the harness is
    /// making progress again); the fail-item flag clears too, since
    /// the tier-3 fire was triggered by a stale <c>last_event_age</c>
    /// and the harness is now responsive.
    /// </summary>
    public void Reset()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            lastEventAt = clock.GetUtcNow();
            currentTier = 0;
            ShouldFailItem = false;
        }
    }

    /// <summary>
    /// True after tier 3 fires — the pump checks this on each
    /// iteration and exits without forwarding the harness's result
    /// (the item has already been failed via <c>api.FailAsync</c>).
    /// </summary>
    public bool ShouldFailItem
    {
        get
        {
            lock (gate)
            {
                return field;
            }
        }

        private set;
    }

    /// <summary>Last <c>last_event_age</c> the tick observed. Diagnostic surface — feeds the
    /// <c>comuki.worker.last_event_age_ms</c> gauge (telemetry is wired in Phase 2).</summary>
    public TimeSpan CurrentLastEventAge
    {
        get
        {
            lock (gate)
            {
                return clock.GetUtcNow() - lastEventAt;
            }
        }
    }

    private void OnTick(object? state)
    {
        TimeSpan elapsed;
        int previousTier;
        bool shouldEscalate;
        int targetTier;

        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            elapsed = clock.GetUtcNow() - lastEventAt;
            previousTier = currentTier;
            targetTier = ComputeTargetTier(elapsed);
            shouldEscalate = targetTier > currentTier;
            if (shouldEscalate)
            {
                currentTier = targetTier;
            }
        }

        if (!shouldEscalate)
        {
            return;
        }

        // Run the tier-specific side effect outside the lock so the
        // gRPC send / api.FailAsync call doesn't block other resets.
        try
        {
            switch (targetTier)
            {
                case 1:
                    FireWarn(elapsed);
                    break;
                case 2:
                    FireGentleKill(elapsed, previousTier);
                    break;
                case 3:
                    FireFailItem(elapsed, previousTier);
                    break;
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "WorkerProgressWatchdog tier {Tier} fire threw on work item {WorkItemId}",
                targetTier,
                run.Claimed.WorkItemId);
        }
    }

    private int ComputeTargetTier(TimeSpan elapsed)
    {
        return elapsed switch
        {
            { } t when t < options.WorkerProgressTimeout => 0,
            { } t when t < options.WorkerProgressTimeout * 2 => 1,
            { } t when t < options.WorkerProgressTimeout * 3 => 2,
            _ => 3,
        };
    }

    private void FireWarn(TimeSpan elapsed)
    {
        if (!options.WorkerProgressEscalationPolicy.HasFlag(WorkerProgressEscalationPolicy.Warn))
        {
            return;
        }

        logger.LogWarning(
            "Stall tier 1 (warn) on work item {WorkItemId}: last event {LastEventAgeMs}ms ago (WorkerProgressTimeout = {WorkerProgressTimeoutMs}ms)",
            run.Claimed.WorkItemId,
            (long)elapsed.TotalMilliseconds,
            (long)options.WorkerProgressTimeout.TotalMilliseconds);

        // Best-effort journal; a send failure is logged but never
        // escalates further (the timer will retry on the next tick if
        // the harness is still silent).
        _ = TrySendStallWarnAsync((long)elapsed.TotalMilliseconds, tier: 1);
    }

    private void FireGentleKill(TimeSpan elapsed, int previousTier)
    {
        if (!options.WorkerProgressEscalationPolicy.HasFlag(WorkerProgressEscalationPolicy.GentleKill))
        {
            return;
        }

        logger.LogWarning(
            "Stall tier 2 (gentle-kill) on work item {WorkItemId}: last event {LastEventAgeMs}ms ago — cancelling harness",
            run.Claimed.WorkItemId,
            (long)elapsed.TotalMilliseconds);

        // Set the same flag the orchestrator's Stop command sets so
        // the loop's "skip complete" path is consistent regardless of
        // whether the Stop came from the orchestrator or the watchdog.
        run.StopRequested = true;
        run.RunCancellation.Cancel();

        // The previous tier already fired its journal; the gentle-kill
        // tier does too (one journal entry per transition, mirrored
        // against the design's "tier 1 / tier 2 / tier 3" labels).
        if (previousTier < 1)
        {
            _ = TrySendStallWarnAsync((long)elapsed.TotalMilliseconds, tier: 2);
        }
    }

    private void FireFailItem(TimeSpan elapsed, int previousTier)
    {
        if (!options.WorkerProgressEscalationPolicy.HasFlag(WorkerProgressEscalationPolicy.FailItem))
        {
            return;
        }

        logger.LogError(
            "Stall tier 3 (fail-item) on work item {WorkItemId}: last event {LastEventAgeMs}ms ago — failing item with reason worker.stall_detected",
            run.Claimed.WorkItemId,
            (long)elapsed.TotalMilliseconds);

        // Same cancel for symmetry with tier 2; tier 2 may not have
        // fired if the policy is e.g. Warn + FailItem (no gentle-kill).
        run.StopRequested = true;
        run.RunCancellation.Cancel();

        lock (gate)
        {
            ShouldFailItem = true;
        }

        // Synchronous fail-item call from the timer thread: the pump
        // is currently awaiting the events iterator and will read
        // ShouldFailItem on its next iteration. The fail-item call
        // here is the actual "complete with failed" side; the pump
        // exits and the loop short-circuits the complete path because
        // run.LeaseLost/StopRequested is true.
        // Best-effort: a non-2xx response is logged; the lease is
        // still cancelled so the loop bails out either way.
        _ = TrySendStallDetectedAsync((long)elapsed.TotalMilliseconds, tier: 3)
            .ContinueWith(
                task =>
                {
                    if (task.Exception is not null)
                    {
                        logger.LogError(
                            task.Exception,
                            "Stall-detected journal send threw on work item {WorkItemId}",
                            run.Claimed.WorkItemId);
                    }
                },
                TaskScheduler.Default);
    }

    private async Task TrySendStallWarnAsync(long lastEventAgeMs, int tier)
    {
        try
        {
            await run.Session.SendAsync(
                WorkerEventEnvelope.ToStallWarnEvent(run.Claimed.WorkItemId, lastEventAgeMs, tier),
                run.RunCancellation.Token);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Stall-warn journal send failed on work item {WorkItemId}",
                run.Claimed.WorkItemId);
        }
    }

    private async Task TrySendStallDetectedAsync(long lastEventAgeMs, int tier)
    {
        var turnElapsedMs = (long)(clock.GetUtcNow() - run.RunStartedAt).TotalMilliseconds;
        var runElapsedMs = (long)(clock.GetUtcNow() - run.ProcessStartedAt).TotalMilliseconds;
        await run.Session.SendAsync(
            WorkerEventEnvelope.ToStallDetectedEvent(
                run.Claimed.WorkItemId,
                lastEventAgeMs,
                turnElapsedMs,
                runElapsedMs,
                tier,
                reason: "worker.stall_detected"),
            run.RunCancellation.Token);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            timer.Dispose();
        }
    }

    /// <summary>Async dispose — the underlying <see cref="Timer"/> exposes
    /// <c>DisposeAsync</c>; we forward so the pump can <c>await using</c>.</summary>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
