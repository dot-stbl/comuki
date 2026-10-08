using Comuki.Shared.Editions.Edition;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Comuki.Shared.Bootstrap.Workers;

/// <summary>
/// The single <see cref="BackgroundService"/> that hosts every
/// <see cref="IComukiWorker"/> registration: one loop per worker with its
/// declared schedule, a fresh DI scope per cycle, and exponential backoff
/// on failure (<c>interval × 2^failures</c>, capped at 10× the interval,
/// reset by a success). An unhandled worker exception is logged and
/// treated like a failing result — the loop keeps the host alive and
/// retries. <see cref="Snapshot"/> exposes the per-worker status for the
/// observability endpoint.
/// </summary>
/// <remarks>
/// <para>
/// **Edition gating** (workstream 6.1 + 6.2): workers whose implementation
/// carries <c>RequiresFeatureAttribute</c> are partitioned at
/// construction through <see cref="WorkerFeatureGate.Partition"/>. Covered
/// workers run from boot; deferred workers wait for the supervisor loop to
/// re-check coverage on <see cref="DeferredRecheckInterval"/>. The 5s
/// default matches <c>LicenseOptions.ReloadDelay</c>'s 5s default so a
/// replaced license file is observed without lag (the file-swap
/// detection is <see cref="LicenseEdition"/>'s own covered behaviour;
/// this loop only consults the runtime <see cref="IEdition"/>).
/// </para>
/// <para>
/// **Hot reload** (only the unlock direction): when a deferred worker
/// becomes covered, the supervisor promotes it into the running set.
/// The shared <c>runtimes</c> dictionary is mutated under the same lock
/// <see cref="Snapshot"/> reads; feature-coverage checks
/// (<see cref="IEdition.Has"/>) run OUTSIDE that lock because the
/// implementation of <c>Has</c> in this project
/// (<c>LicenseEdition.EnsureFresh</c>) does sync-over-async IO + crypto
/// and would otherwise stall every Snapshot() reader while a cycle
/// recomputes the snapshot. The reverse (demotion on downgrade) is out
/// of scope for this workstream — the worker keeps running until the
/// host restarts.
/// </para>
/// </remarks>
/// <param name="registeredWorkers">Every <see cref="IComukiWorker"/> registration; modules add theirs through their installers.</param>
/// <param name="scopeFactory">Opens the per-cycle scope (scoped stores die with the cycle).</param>
/// <param name="clock">Clock for run/next-run timestamps.</param>
/// <param name="loggerFactory">Creates the per-worker logger categories.</param>
/// <param name="logger">Registry-level logger.</param>
/// <param name="edition">Runtime edition; null when the editions layer is not wired (any gated marker is then a wiring gap and throws at construction).</param>
public sealed class ComukiWorkerRegistry(
    IEnumerable<IComukiWorker> registeredWorkers,
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILoggerFactory loggerFactory,
    ILogger<ComukiWorkerRegistry> logger,
    IEdition? edition = null) : BackgroundService
{
    // Partition is computed exactly once at construction via the static
    // helper below; both fields are derived from it in the same call
    // so per-deferred-worker Warning lines are not logged twice.
    private readonly InitialState initial = ComukiWorkerRegistryHelpers.BuildInitial(registeredWorkers, edition, logger);

    // Loops the supervisor started for promoted deferred workers. Only the
    // supervisor thread mutates it; ExecuteAsync reads it after awaiting
    // the supervisor, so the task-completion ordering makes the handoff
    // safe without another lock.
    private readonly List<Task> promotedLoops = [];

    private Dictionary<string, WorkerRuntime> runtimes => initial.Runtimes;
    private List<WorkerFeatureGate.DeferredWorker> deferred => initial.Deferred;

    private WorkerLoopRunner? loopRunner;

    /// <summary>
    /// Interval between supervisor polls of <see cref="IEdition"/> for
    /// deferred workers. Default 5s matches
    /// <c>LicenseOptions.ReloadDelay</c>'s default — no
    /// options dependency is added so the bootstrap layer stays
    /// configuration-agnostic. InternalsVisibleTo the unit project so
    /// tests shrink it to a few ms; never set it below the throttle
    /// window in production or the supervisor will fight
    /// <see cref="LicenseEdition"/>'s own reload delay.
    /// </summary>
    internal TimeSpan DeferredRecheckInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Point-in-time status of every registered worker.</summary>
    /// <returns>One <see cref="WorkerStatus"/> per worker, ordered by name.</returns>
    public IReadOnlyList<WorkerStatus> Snapshot()
    {
        lock (runtimes)
        {
            return [.. runtimes.Values
                .Select(static runtime => runtime.Status())
                .OrderBy(static status => status.Name, StringComparer.Ordinal)];
        }
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Lazy: don't construct the runner until we actually have
        // workers — HostZeroWorkersGracefullyAsync test relies on this
        // short-circuit firing without ever touching the loop surface.
        loopRunner = new WorkerLoopRunner(
            scopeFactory: scopeFactory,
            clock: clock,
            loggerFactory: loggerFactory,
            promotedLoops: promotedLoops,
            runtimes: runtimes,
            deferred: deferred,
            edition: edition,
            logger: logger);

        // The zero-workers early-return must hold for BOTH the included
        // and the deferred buckets — a host that registered only deferred
        // workers must still exit cleanly when those workers never become
        // covered (the existing HostZeroWorkersGracefullyAsync test relies
        // on this branch firing when no loop will ever start).
        if (runtimes.Count == 0 && deferred.Count == 0)
        {
            logger.LogInformation("no comuki workers registered");
            return;
        }

        logger.LogInformation(
            "comuki worker registry hosting {WorkerCount} worker(s): {WorkerNames}",
            runtimes.Count,
            string.Join(", ", runtimes.Keys.Order(StringComparer.Ordinal)));

        // Loops running at boot — they receive their runtime by reference
        // so they never touch the dictionary directly (the dictionary can
        // gain entries at runtime when the supervisor promotes deferred
        // workers, so lookups by name are unsafe outside the supervisor).
        var startedAtBoot = runtimes.Values
            .Select(runtime => loopRunner.RunAsync(runtime, stoppingToken))
            .ToArray();

        var supervisor = loopRunner.SuperviseDeferredAsync(stoppingToken, DeferredRecheckInterval);

        await Task.WhenAll([.. startedAtBoot, supervisor]);

        // The supervisor has exited (stopping, or every deferred worker
        // promoted); its loops are not in startedAtBoot, so await them
        // here — StopAsync then waits for a promoted worker's in-flight
        // cycle before the host disposes the service provider.
        await Task.WhenAll([.. promotedLoops]);
    }

    /// <summary>Boot-time state bundle — both pieces come from one Partition call so the warning log lines are emitted exactly once per deferred worker.</summary>
    internal sealed record InitialState(Dictionary<string, WorkerRuntime> Runtimes, List<WorkerFeatureGate.DeferredWorker> Deferred);

    /// <summary>One promotion decided outside the <c>runtimes</c> lock and applied under it: the deferred entry and the runtime created for it.</summary>
    internal sealed record Promotion(WorkerFeatureGate.DeferredWorker Deferred, WorkerRuntime Runtime);

    /// <summary>Mutable per-worker state, guarded by <see cref="Sync"/>; mutated by the worker's loop, read by <see cref="Snapshot"/>.</summary>
    internal sealed class WorkerRuntime(IComukiWorker worker)
    {
        public IComukiWorker Worker => worker;

        public object Sync { get; } = new();

        public DateTimeOffset? LastRunAt { get; set; }

        public DateTimeOffset? NextRunAt { get; set; }

        public WorkerResult? LastResult { get; set; }

        public int Failures { get; set; }

        public WorkerStatus Status()
        {
            return new WorkerStatus(worker.Name, LastRunAt, NextRunAt, LastResult, Failures, IsHealthy: Failures == 0);
        }
    }
}

/// <summary>
/// Pure helpers backing <see cref="ComukiWorkerRegistry"/>: backoff
/// math + boot partition. Stateless, so they live next to the registry
/// rather than as private members (per the no-private-methods rule).
/// </summary>
file static class ComukiWorkerRegistryHelpers
{
    /// <summary>Cap on the exponential factor; matches the registries of the <c>interval × 2^failures</c> curve.</summary>
    private const int MaxBackoffFactor = 10;

    /// <summary>
    /// interval × 2^failures, capped at <see cref="MaxBackoffFactor"/>× the interval. A
    /// fresh worker (failures = 0) keeps its plain interval. The exponent is
    /// clamped first so the shift cannot overflow before the cap bites.
    /// </summary>
    public static TimeSpan BackoffDelay(TimeSpan interval, int failures)
    {
        var factor = Math.Min(1L << Math.Min(failures, 30), MaxBackoffFactor);

        return TimeSpan.FromTicks(interval.Ticks * factor);
    }

    /// <summary>
    /// One pass over the worker list to build the runtimes map and the
    /// deferred list; both come out of the same
    /// <see cref="WorkerFeatureGate.PartitionWorkers"/> call so per-deferred-
    /// worker Warning lines are not logged twice.
    /// </summary>
    public static ComukiWorkerRegistry.InitialState BuildInitial(
        IEnumerable<IComukiWorker> workers,
        IEdition? edition,
        ILogger logger)
    {
        var partition = WorkerFeatureGate.PartitionWorkers(workers, edition, logger);
        var runtimes = new Dictionary<string, ComukiWorkerRegistry.WorkerRuntime>(StringComparer.Ordinal);
        foreach (var ungated in partition.Ungated)
        {
            runtimes[ungated.Name] = new ComukiWorkerRegistry.WorkerRuntime(ungated);
        }

        foreach (var covered in partition.Covered)
        {
            runtimes[covered.Worker.Name] = new ComukiWorkerRegistry.WorkerRuntime(covered.Worker);
        }

        return new ComukiWorkerRegistry.InitialState(runtimes, [.. partition.Deferred]);
    }
}

/// <summary>
/// Per-cycle loop orchestrator for <see cref="ComukiWorkerRegistry"/>:
/// the deferred-worker supervisor, the worker-loop body, and one cycle
/// execution. Lives next to the registry because its state shape — a
/// shared runtimes dictionary, the deferred list, the promoted-loops
/// tracking, and the per-worker runtime — is the registry's domain, not
/// the loop runner's. Extracted from the registry so the registry stays
/// a thin lifecycle surface (Snapshot + ExecuteAsync override).
/// </summary>
internal sealed class WorkerLoopRunner(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILoggerFactory loggerFactory,
    IList<Task> promotedLoops,
    Dictionary<string, ComukiWorkerRegistry.WorkerRuntime> runtimes,
    List<WorkerFeatureGate.DeferredWorker> deferred,
    IEdition? edition,
    ILogger logger)
{
    private readonly IServiceScopeFactory scopeFactory = scopeFactory;
    private readonly TimeProvider clock = clock;
    private readonly ILoggerFactory loggerFactory = loggerFactory;
    private readonly ILogger logger = logger;

    private readonly Dictionary<string, ComukiWorkerRegistry.WorkerRuntime> runtimes = runtimes;
    private readonly List<WorkerFeatureGate.DeferredWorker> deferred = deferred;
    private readonly IList<Task> promotedLoops = promotedLoops;
    private readonly IEdition? edition = edition;

    /// <summary>
    /// Polls the runtime edition for newly-covered feature gates and
    /// promotes the matching deferred workers into the running set.
    /// Exits on stoppingToken cancellation or once the deferred list is
    /// empty (every gate either covered or abandoned).
    /// </summary>
    public async Task SuperviseDeferredAsync(CancellationToken stoppingToken, TimeSpan recheckInterval)
    {
        while (!stoppingToken.IsCancellationRequested && deferred.Count > 0)
        {
            try
            {
                await Task.Delay(recheckInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            // Coverage checks (IEdition.Has → LicenseEdition.EnsureFresh →
            // sync-over-async IO + crypto) run OUTSIDE the runtimes lock so
            // a slow verifier cannot stall Snapshot() readers or block a
            // future promotion. Snapshot() is the only other reader of
            // runtimes; the supervisor is the only writer of deferred.
            // We collect coverage decisions for a snapshot of the current
            // deferred list, then re-take the lock to mutate state.
            var snapshot = deferred.ToArray();

            // Defensive copy of the decisions list; the loop below may also
            // flip the result mid-iteration if the catalog/state changes
            // between calls (not expected in practice but consistent with
            // the "evaluate outside, mutate inside" split).
            var promotions = new List<WorkerFeatureGate.DeferredWorker>();
            if (edition is { } current)
            {
                foreach (var candidate in snapshot)
                {
                    if (current.Has(candidate.Feature))
                    {
                        promotions.Add(candidate);
                    }
                }
            }

            // Mutate runtimes + deferred under the lock. Capture the runtime
            // we just created so the start below doesn't have to re-look-up
            // runtimes (which is unsafe without the lock — Snapshot may
            // have read it concurrently, but never writes).
            var startArgs = new List<ComukiWorkerRegistry.Promotion>(promotions.Count);
            lock (runtimes)
            {
                for (var index = deferred.Count - 1; index >= 0; index--)
                {
                    if (promotions.Count == 0)
                    {
                        break;
                    }

                    var candidate = deferred[index];
                    var promotionIndex = promotions.IndexOf(candidate);
                    if (promotionIndex < 0)
                    {
                        continue;
                    }

                    var runtime = new ComukiWorkerRegistry.WorkerRuntime(candidate.Worker);
                    runtimes[candidate.Worker.Name] = runtime;
                    deferred.RemoveAt(index);
                    startArgs.Add(new ComukiWorkerRegistry.Promotion(candidate, runtime));
                }
            }

            // Start the promoted loops OUTSIDE the lock so a hot loop
            // doesn't starve Snapshot() reads or future promotions. The
            // tasks are tracked in promotedLoops so ExecuteAsync awaits
            // them after the supervisor exits — a promoted worker's
            // in-flight cycle is never abandoned by shutdown.
            foreach (var promotion in startArgs)
            {
                logger.LogInformation(
                    "worker {WorkerName} feature {FeatureKey} now covered; starting",
                    promotion.Deferred.Worker.Name,
                    promotion.Deferred.FeatureKey);

                promotedLoops.Add(RunAsync(promotion.Runtime, stoppingToken));
            }
        }
    }

    /// <summary>
    /// Per-worker loop body: startup workers run once, everything else
    /// polls on its schedule interval with exponential backoff on
    /// failures. The runner's clock drives the NextRunAt stamps;
    /// Snapshot() reads them under <see cref="ComukiWorkerRegistry.WorkerRuntime.Sync"/>.
    /// </summary>
    public async Task RunAsync(ComukiWorkerRegistry.WorkerRuntime runtime, CancellationToken stoppingToken)
    {
        var worker = runtime.Worker;

        // Startup workers run exactly once; everything else polls on its
        // interval. Both run their first cycle immediately.
        if (worker.Schedule is WorkerSchedule.StartupWorkerSchedule)
        {
            await ExecuteCycleAsync(runtime, stoppingToken);
            return;
        }

        var interval = ((WorkerSchedule.IntervalWorkerSchedule)worker.Schedule).PollInterval;

        while (!stoppingToken.IsCancellationRequested)
        {
            await ExecuteCycleAsync(runtime, stoppingToken);

            var delay = ComukiWorkerRegistryHelpers.BackoffDelay(interval, runtime.Failures);
            lock (runtime.Sync)
            {
                runtime.NextRunAt = clock.GetUtcNow() + delay;
            }

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Opens one DI scope, runs the worker's cycle, writes back the
    /// outcome to <see cref="ComukiWorkerRegistry.WorkerRuntime"/> under the
    /// per-runtime sync. Exception → failed cycle, never host crash.
    /// </summary>
    public async Task ExecuteCycleAsync(ComukiWorkerRegistry.WorkerRuntime runtime, CancellationToken stoppingToken)
    {
        lock (runtime.Sync)
        {
            runtime.LastRunAt = clock.GetUtcNow();
            runtime.NextRunAt = null;
        }

        try
        {
            WorkerResult result;

            await using (var scope = scopeFactory.CreateAsyncScope())
            {
                var context = new WorkerContext(scope.ServiceProvider, clock, loggerFactory.CreateLogger(runtime.Worker.Name));
                result = await runtime.Worker.ExecuteAsync(context, stoppingToken);
            }

            lock (runtime.Sync)
            {
                runtime.LastResult = result;
                runtime.NextRunAt = null;

                if (result.Success)
                {
                    if (runtime.Failures > 0)
                    {
                        logger.LogInformation("worker {WorkerName} recovered after {FailureCount} failure(s)", runtime.Worker.Name, runtime.Failures);
                    }

                    runtime.Failures = 0;
                }
                else
                {
                    runtime.Failures++;
                    logger.LogWarning("worker {WorkerName} reported failure: {FailureDetail}", runtime.Worker.Name, result.Detail);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // The registry is the loop's top-level handler: an unhandled
            // worker exception is a failed cycle, not a host crash — log,
            // count, back off, retry.
            lock (runtime.Sync)
            {
                runtime.Failures++;
                runtime.LastResult = WorkerResult.Fail(exception.GetType().Name);
                runtime.NextRunAt = null;
            }

            logger.LogError(exception, "worker {WorkerName} threw ({FailureCount} consecutive failure(s))", runtime.Worker.Name, runtime.Failures);
        }
    }
}
