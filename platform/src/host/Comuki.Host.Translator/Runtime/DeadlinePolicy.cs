using Comuki.Host.Translator.Execution.Loop;
using Comuki.Host.Translator.Execution.Run;
using Microsoft.Extensions.Options;

namespace Comuki.Host.Translator.Runtime;

/// <summary>
/// Wall-clock deadline policy (harden-worker-runtime Phase 1, design D2).
/// Two budgets enforce hard upper bounds on cycle and process
/// lifetime, distinct from the progress-watchdog's
/// <c>last_event_age</c> input:
/// <list type="bullet">
///   <item><see cref="TranslatorOptions.TurnBudget"/> — time from
///   <see cref="WorkerRun.RunStartedAt"/> to a final
///   <c>StageReport</c>. First breach = gentle-kill (cancel the
///   harness; idempotent restart). Three consecutive breaches inside
///   the same worker process = fail-item.</item>
///   <item><see cref="TranslatorOptions.RunBudget"/> — time from
///   <see cref="WorkerRun.ProcessStartedAt"/> to a final
///   <c>StageReport</c>. Single breach = fail-item (one budget
///   ceiling per process; the operator / scheduler is the
///   re-spawning side).</item>
/// </list>
/// Both budgets share the same escalation path as the
/// <see cref="WorkerProgressWatchdog"/> (cancel + journal + the
/// loop's existing <c>api.FailAsync</c> call on tier 3). Brain-ops
/// 5/15/30-min budgets live in <c>add-mission-cowork</c> and have
/// a different semantic (task-completion, not worker ephemeral
/// lifetime) — see <c>design.md</c> §Coordination notes.
/// </summary>
/// <remarks>
/// The consecutive-turn-breach counter lives on
/// <see cref="DeadlineChainState"/>, not on the policy instance,
/// so the chain is "inside the same worker process" (per the
/// design's wording) — the pump recreates the policy every
/// cycle, but the state survives. The pump resets the counter on
/// a successful cycle completion; the catch-OCE branch
/// (cancellation) does not reset it.
/// </remarks>
public sealed class DeadlinePolicy : IDisposable
{
    private readonly WorkerRun run;
    private readonly DeadlineChainState chainState;
    private readonly TranslatorOptions options;
    private readonly TimeProvider clock;
    private readonly ILogger<DeadlinePolicy> logger;
    private readonly TimeSpan tickInterval;
    private readonly Lock gate = new();
    private ITimer? timer;
    private bool runBudgetFired;
    private bool disposed;

    /// <summary>
    /// Constructs the policy. The constructor does NOT start the
    /// timer; the pump drives that via <see cref="Start"/> so the
    /// first tick reads the actual wall-clock at cycle-spawn time.
    /// </summary>
    /// <param name="run">The bound run — the policy reads
    /// <see cref="WorkerRun.RunStartedAt"/> and
    /// <see cref="WorkerRun.ProcessStartedAt"/> for the
    /// wall-clock budgets and cancels
    /// <see cref="WorkerRun.RunCancellation"/> on gentle-kill /
    /// fail-item.</param>
    /// <param name="chainState">Process-level state (counter +
    /// threshold) the policy reads and writes. The counter
    /// survives across policy recreations because the state
    /// outlives the policy.</param>
    /// <param name="options">Bound <c>Translator</c> options — carries
    /// <c>TurnBudget</c>, <c>RunBudget</c> and
    /// <c>ConsecutiveTurnBreachesBeforeFail</c>.</param>
    /// <param name="clock">Injected for tests; the policy never reads
    /// <c>DateTimeOffset.UtcNow</c> directly. The clock's
    /// <see cref="TimeProvider.CreateTimer"/> implementation drives the
    /// tick.</param>
    /// <param name="logger">Records the budget breaches; gentle-kill is
    /// warning, fail-item is error.</param>
    public DeadlinePolicy(
        WorkerRun run,
        DeadlineChainState chainState,
        IOptions<TranslatorOptions> options,
        TimeProvider clock,
        ILogger<DeadlinePolicy> logger)
    {
        this.run = run;
        this.chainState = chainState;
        this.options = options.Value;
        this.clock = clock;
        this.logger = logger;
        // Tick every (min(TurnBudget, RunBudget) / 12) so a 60-min
        // turn budget checks every 5 min, a 5-min turn budget checks
        // every 25s. Floors at 100ms (small enough for unit tests —
        // the timer fires on virtual-time advance) and caps at 30s
        // (a 1h+ budget still ticks every 30s).
        var smallest = this.options.TurnBudget < this.options.RunBudget
            ? this.options.TurnBudget
            : this.options.RunBudget;
        tickInterval = TimeSpan.FromMilliseconds(
            Math.Clamp(smallest.TotalMilliseconds / 12, 100, 30_000));
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

            ShouldFailItem = false;
            FailReason = null;
            timer ??= clock.CreateTimer(_ => OnTick(), state: null, dueTime: Timeout.InfiniteTimeSpan, period: Timeout.InfiniteTimeSpan);
            timer.Change(tickInterval, tickInterval);
        }
    }

    /// <summary>Disarms the timer; the pump calls this when the cycle ends.</summary>
    public void Stop()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>
    /// True after a turn-budget chain reached tier 3 (fail-item) or
    /// after a run-budget breach. The pump reads this on each
    /// iteration and exits without forwarding the harness's result.
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

    /// <summary>The typed reason attached to the last fail-item fire —
    /// one of <c>worker.turn_budget_exceeded</c> /
    /// <c>worker.run_budget_exceeded</c>. <c>null</c> until the
    /// policy has fired at least once.</summary>
    public string? FailReason
    {
        get
        {
            lock (gate)
            {
                return field;
            }
        }

        private set
        {
            lock (gate)
            {
                field = value;
            }
        }
    }

    private void OnTick()
    {
        var now = clock.GetUtcNow();
        var turnElapsed = now - run.RunStartedAt;
        var runElapsed = now - run.ProcessStartedAt;

        var turnExceeded = turnElapsed >= options.TurnBudget;
        var runExceeded = runElapsed >= options.RunBudget;

        if (!turnExceeded && !runExceeded)
        {
            return;
        }

        // Run-budget is one-shot: a single breach fails the item
        // regardless of the turn-budget chain.
        if (runExceeded && !runBudgetFired)
        {
            lock (gate)
            {
                if (disposed || runBudgetFired)
                {
                    return;
                }

                runBudgetFired = true;
                ShouldFailItem = true;
                FailReason = "worker.run_budget_exceeded";
            }

            FireRunBudgetBreach(turnElapsed, runElapsed);
            return;
        }

        if (!turnExceeded)
        {
            return;
        }

        // Turn-budget chain: each tick that finds turnElapsed past
        // the budget counts as a breach. The counter lives on
        // DeadlineChainState so the chain survives policy
        // recreations. After ConsecutiveTurnBreachesBeforeFail
        // consecutive breaches, the item is failed with reason
        // worker.turn_budget_exceeded.
        int observedBreaches;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }
        }

        chainState.ConsecutiveBreaches++;
        observedBreaches = chainState.ConsecutiveBreaches;

        FireTurnBudgetBreach(turnElapsed, runElapsed, observedBreaches);

        if (observedBreaches >= chainState.ConsecutiveTurnBreachesBeforeFail)
        {
            lock (gate)
            {
                if (disposed)
                {
                    return;
                }

                ShouldFailItem = true;
                FailReason = "worker.turn_budget_exceeded";
            }
        }
    }

    private void FireTurnBudgetBreach(TimeSpan turnElapsed, TimeSpan runElapsed, int observedBreaches)
    {
        if (observedBreaches >= chainState.ConsecutiveTurnBreachesBeforeFail)
        {
            logger.LogError(
                "Turn-budget breach #{ObservedBreaches} (>= {Threshold}) on work item {WorkItemId}: turn {TurnElapsedMs}ms >= TurnBudget {TurnBudgetMs}ms — failing item with reason worker.turn_budget_exceeded",
                observedBreaches,
                chainState.ConsecutiveTurnBreachesBeforeFail,
                run.Claimed.WorkItemId,
                (long)turnElapsed.TotalMilliseconds,
                (long)options.TurnBudget.TotalMilliseconds);

            run.StopRequested = true;
            run.RunCancellation.Cancel();

            _ = TrySendStallDetectedAsync(
                lastEventAgeMs: 0,
                turnElapsedMs: (long)turnElapsed.TotalMilliseconds,
                runElapsedMs: (long)runElapsed.TotalMilliseconds,
                tier: 3,
                reason: "worker.turn_budget_exceeded")
                .ContinueWith(
                    task =>
                    {
                        if (task.Exception is not null)
                        {
                            logger.LogError(
                                task.Exception,
                                "Turn-budget stall-detected journal send threw on work item {WorkItemId}",
                                run.Claimed.WorkItemId);
                        }
                    },
                    TaskScheduler.Default);
            return;
        }

        logger.LogWarning(
            "Turn-budget breach #{ObservedBreaches} on work item {WorkItemId}: turn {TurnElapsedMs}ms >= TurnBudget {TurnBudgetMs}ms — gentle-kill (consecutive {ObservedBreaches}/{Threshold})",
            observedBreaches,
            chainState.ConsecutiveTurnBreachesBeforeFail,
            run.Claimed.WorkItemId,
            (long)turnElapsed.TotalMilliseconds,
            (long)options.TurnBudget.TotalMilliseconds);

        run.StopRequested = true;
        run.RunCancellation.Cancel();

        // Gentle-kill tier for the turn-budget chain mirrors the
        // watchdog's tier 2: cancel the harness, log + journal, no
        // fail-item. The next cycle's successful completion resets
        // the counter via ResetBreachCounter below.
    }

    /// <summary>
    /// Resets the consecutive-breach counter on a successful
    /// cycle completion. The pump calls this after the events
    /// iterator returns without the watchdog / policy firing.
    /// Delegates to <see cref="DeadlineChainState"/> so the reset
    /// actually clears the process-level counter (not a
    /// policy-local copy that the next policy would re-read).
    /// </summary>
    public void ResetBreachCounter()
    {
        chainState.ConsecutiveBreaches = 0;
    }

    private void FireRunBudgetBreach(TimeSpan turnElapsed, TimeSpan runElapsed)
    {
        logger.LogError(
            "Run-budget breach on work item {WorkItemId}: run {RunElapsedMs}ms >= RunBudget {RunBudgetMs}ms — failing item with reason worker.run_budget_exceeded",
            run.Claimed.WorkItemId,
            (long)runElapsed.TotalMilliseconds,
            (long)options.RunBudget.TotalMilliseconds);

        run.StopRequested = true;
        run.RunCancellation.Cancel();

        _ = TrySendStallDetectedAsync(
            lastEventAgeMs: 0,
            turnElapsedMs: (long)turnElapsed.TotalMilliseconds,
            runElapsedMs: (long)runElapsed.TotalMilliseconds,
            tier: 3,
            reason: "worker.run_budget_exceeded")
            .ContinueWith(
                    task =>
                    {
                        if (task.Exception is not null)
                        {
                            logger.LogError(
                                task.Exception,
                                "Run-budget stall-detected journal send threw on work item {WorkItemId}",
                                run.Claimed.WorkItemId);
                        }
                    },
                    TaskScheduler.Default);
    }

    private async Task TrySendStallDetectedAsync(long lastEventAgeMs, long turnElapsedMs, long runElapsedMs, int tier, string reason)
    {
        try
        {
            await run.Session.SendAsync(
                WorkerEventEnvelope.ToStallDetectedEvent(
                    run.Claimed.WorkItemId,
                    lastEventAgeMs,
                    turnElapsedMs,
                    runElapsedMs,
                    tier,
                    reason),
                run.RunCancellation.Token);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Stall-detected journal send failed on work item {WorkItemId} (reason {Reason})",
                run.Claimed.WorkItemId,
                reason);
        }
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
            timer?.Dispose();
        }
    }

    /// <summary>Async dispose — the underlying <see cref="ITimer"/> exposes
    /// <c>DisposeAsync</c>; we forward so the pump can <c>await using</c>.</summary>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
