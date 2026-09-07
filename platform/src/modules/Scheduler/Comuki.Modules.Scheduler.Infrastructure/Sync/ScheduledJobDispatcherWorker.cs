using System.Data.Common;
using System.Text.Json;
using Comuki.Modules.Scheduler.Application.Observers;
using Comuki.Modules.Scheduler.Application.Options;
using Comuki.Modules.Scheduler.Application.Ports;
using Comuki.Modules.Scheduler.Domain.Jobs;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Comuki.Shared.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comuki.Modules.Scheduler.Infrastructure.Sync;

/// <summary>
/// Polls <see cref="IScheduledJobStore"/> every
/// <see cref="SchedulerOptions.PollInterval"/> for due jobs, dispatches
/// one run per job through <see cref="ISchedulerDispatcher"/> and stamps
/// the fire trail. Single-fire semantics: the store's due query uses
/// <c>FOR UPDATE SKIP LOCKED</c>, so two host replicas never double-fire
/// the same job on the same tick.
/// <para>
/// Side-channel: every successful fire is fanned out to the registered
/// <see cref="ISchedulerObserver"/> implementations (journal row,
/// Sentry capture, future chat notifier). Observers are best-effort — a
/// thrown observer logs and the rest of the batch continues.
/// </para>
/// <para>
/// The worker is a singleton singleton hosted service — no per-request
/// state, no DI graph held beyond what <see cref="IServiceScopeFactory"/>
/// resolves per cycle.
/// </para>
/// </summary>
/// <param name="scopeFactory">Scope factory — resolves the scoped store + dispatcher per cycle.</param>
/// <param name="scopeAccessor">AmbientScope — the dispatcher runs toAsSystem so journal appends have a clear actor.</param>
/// <param name="clock">Wall-clock source for the polling cadence.</param>
/// <param name="options">Tunables (poll interval, batch size).</param>
/// <param name="observers">Side-channel observers the dispatcher notifies after every fire.</param>
/// <param name="logger">Structured logger.</param>
public sealed class ScheduledJobDispatcherWorker(
    IServiceScopeFactory scopeFactory,
    ISubjectScopeAccessor scopeAccessor,
    TimeProvider clock,
    IOptions<SchedulerOptions> options,
    IEnumerable<ISchedulerObserver> observers,
    ILogger<ScheduledJobDispatcherWorker> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Scheduled job dispatcher started (poll interval {IntervalSeconds}s, batch {Batch})",
            options.Value.PollInterval.TotalSeconds,
            options.Value.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var systemScope = scopeAccessor.AsSystem("scheduler-dispatcher");
                var processed = await PollOnceAsync(stoppingToken);
                if (processed > 0)
                {
                    logger.LogInformation("Scheduled job dispatcher fired {Count} job(s)", processed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (exception is HttpRequestException or TimeoutException
                                       or DbException or JsonException)
            {
                // boundary: the worker's own supervision loop — transient
                // store / dispatcher failures must not kill the hosted
                // service. Next cycle retries the whole batch.
                logger.LogError(exception, "Scheduled job dispatcher cycle failed; retrying next interval");
            }

            try
            {
                await Task.Delay(options.Value.PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        logger.LogInformation("Scheduled job dispatcher stopped");
    }

    /// <summary>
    /// Runs one polling cycle synchronously — exposed for integration
    /// tests that need to drive the dispatcher deterministically rather
    /// than wait for the 30-second interval.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns>Number of jobs fired in this cycle.</returns>
    public async Task<int> PollOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IScheduledJobStore>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<ISchedulerDispatcher>();

        var now = clock.GetUtcNow();
        var due = await store.ListDueAsync(now, options.Value.BatchSize, cancellationToken);
        var fired = 0;

        foreach (var job in due)
        {
            try
            {
                var runId = await dispatcher.DispatchAsync(job, cancellationToken);
                job.MarkFired(now);
                await store.UpdateAsync(job, cancellationToken);

                fired++;
                logger.LogInformation(
                    "Scheduled job {JobId} fired run {RunId} (profile={ProfileKey}, cron={CronExpression})",
                    job.Id.Value, runId.Value, job.ProfileKey, job.CronExpression);

                await DispatcherObservers.NotifyFiredAsync(
                    observers,
                    job,
                    runId,
                    now,
                    logger,
                    cancellationToken);

                ComukiTelemetry.RunsStarted.Add(1);
            }
            catch (Exception exception) when (exception is HttpRequestException or TimeoutException
                                       or DbException or JsonException)
            {
                // per-job isolation — one failing dispatch schedules a
                // retry on the next cycle (the job stays due) and never
                // stops the rest of the batch
                logger.LogWarning(exception,
                    "Scheduled job {JobId} dispatch failed; will retry on next cycle",
                    job.Id.Value);
            }
        }

        return fired;
    }
}

/// <summary>
/// File-scoped static helpers for the dispatcher worker. The
/// observer-fan-out logic is a pure pipeline (no DI dependencies of
/// its own beyond what the worker passes in) and a single static
/// method is the smallest unit that keeps the worker free of private
/// methods (rule code-shape §9 / class-layout-and-tooling §1a).
/// </summary>
file static class DispatcherObservers
{
    /// <summary>
    /// Fans a single fire out to every registered observer. Each observer
    /// runs in isolation — one throwing observer logs a warning and the
    /// rest of the observers still fire.
    /// </summary>
    /// <param name="observers">Side-channel observers registered in DI.</param>
    /// <param name="job">The job that fired.</param>
    /// <param name="runId">Id of the orchestration run the dispatcher created.</param>
    /// <param name="firedAt">Wall-clock instant the fire was stamped.</param>
    /// <param name="logger">Structured logger for the per-observer catch.</param>
    /// <param name="cancellationToken"></param>
    public static async Task NotifyFiredAsync(
        IEnumerable<ISchedulerObserver> observers,
        ScheduledJob job,
        RunId runId,
        DateTimeOffset firedAt,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        foreach (var observer in observers)
        {
            try
            {
                await observer.OnJobFiredAsync(
                    job.Id,
                    job.ProjectId,
                    job.ProfileKey,
                    runId,
                    firedAt,
                    cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or TimeoutException
                                       or DbException or JsonException)
            {
                // boundary: observers are best-effort side-channels. The
                // run + fire trail are already persisted; an observer
                // failure must not abort the cycle or the remaining
                // observers.
                logger.LogWarning(exception,
                    "Scheduler observer {ObserverType} threw; fire is unaffected",
                    observer.GetType().Name);
            }
        }
    }
}
