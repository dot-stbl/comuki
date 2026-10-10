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
/// <param name="registeredWorkers">Every <see cref="IComukiWorker"/> registration; modules add theirs through their installers.</param>
/// <param name="scopeFactory">Opens the per-cycle scope (scoped stores die with the cycle).</param>
/// <param name="clock">Clock for run/next-run timestamps.</param>
/// <param name="loggerFactory">Creates the per-worker logger categories.</param>
/// <param name="logger">Registry-level logger.</param>
public sealed class ComukiWorkerRegistry(
    IEnumerable<IComukiWorker> registeredWorkers,
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILoggerFactory loggerFactory,
    ILogger<ComukiWorkerRegistry> logger) : BackgroundService
{
    private const int MaxBackoffFactor = 10;

    private readonly Dictionary<string, WorkerRuntime> runtimes = registeredWorkers
        .GroupBy(static worker => worker.Name, StringComparer.Ordinal)
        .ToDictionary(static group => group.Key, static group => new WorkerRuntime(group.First()), StringComparer.Ordinal);

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
        if (runtimes.Count == 0)
        {
            logger.LogInformation("no comuki workers registered");
            return;
        }

        logger.LogInformation("comuki worker registry hosting {WorkerCount} worker(s): {WorkerNames}",
            runtimes.Count,
            string.Join(", ", runtimes.Keys.Order(StringComparer.Ordinal)));

        var loops = runtimes.Values
            .Select(static runtime => runtime.Worker)
            .Select(worker => RunWorkerAsync(worker, stoppingToken))
            .ToArray();

        await Task.WhenAll(loops);
    }

    private async Task RunWorkerAsync(IComukiWorker worker, CancellationToken stoppingToken)
    {
        // Startup workers run exactly once; everything else polls on its
        // interval. Both run their first cycle immediately.
        if (worker.Schedule is WorkerSchedule.StartupWorkerSchedule)
        {
            await ExecuteCycleAsync(worker, stoppingToken);
            return;
        }

        var interval = ((WorkerSchedule.IntervalWorkerSchedule)worker.Schedule).PollInterval;

        while (!stoppingToken.IsCancellationRequested)
        {
            await ExecuteCycleAsync(worker, stoppingToken);

            var delay = BackoffDelay(interval, runtimes[worker.Name].Failures);
            runtimes[worker.Name].NextRunAt = clock.GetUtcNow() + delay;

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

    private async Task ExecuteCycleAsync(IComukiWorker worker, CancellationToken stoppingToken)
    {
        var runtime = runtimes[worker.Name];

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
                var context = new WorkerContext(scope.ServiceProvider, clock, loggerFactory.CreateLogger(worker.Name));
                result = await worker.ExecuteAsync(context, stoppingToken);
            }

            lock (runtime.Sync)
            {
                runtime.LastResult = result;
                runtime.NextRunAt = null;

                if (result.Success)
                {
                    if (runtime.Failures > 0)
                    {
                        logger.LogInformation("worker {WorkerName} recovered after {FailureCount} failure(s)", worker.Name, runtime.Failures);
                    }

                    runtime.Failures = 0;
                }
                else
                {
                    runtime.Failures++;
                    logger.LogWarning("worker {WorkerName} reported failure: {FailureDetail}", worker.Name, result.Detail);
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

            logger.LogError(exception, "worker {WorkerName} threw ({FailureCount} consecutive failure(s))", worker.Name, runtime.Failures);
        }
    }

    private static TimeSpan BackoffDelay(TimeSpan interval, int failures)
    {
        // interval x 2^failures, capped at 10x the interval; a fresh
        // worker (failures = 0) keeps its plain interval. The exponent is
        // clamped first so the shift cannot overflow before the cap bites.
        var factor = Math.Min(1L << Math.Min(failures, 30), MaxBackoffFactor);

        return TimeSpan.FromTicks(interval.Ticks * factor);
    }

    /// <summary>Mutable per-worker state, guarded by <see cref="Sync"/>; mutated by the worker's loop, read by <see cref="Snapshot"/>.</summary>
    private sealed class WorkerRuntime(IComukiWorker worker)
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
