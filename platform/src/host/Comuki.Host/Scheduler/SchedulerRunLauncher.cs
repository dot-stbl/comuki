using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Domain.WorkItems;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Modules.Scheduler.Application.Ports;
using Comuki.Modules.Scheduler.Domain.Jobs;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.Options;

namespace Comuki.Host.Scheduler;

/// <summary>
/// Host-side <see cref="ISchedulerDispatcher"/>: turns a single
/// <see cref="ScheduledJob"/> into one <c>Run</c> + one queued
/// <c>WorkItem</c>. The brief jsonb the operator wrote into the job
/// becomes the worker's <c>brief</c> column verbatim — the worker
/// runtime cannot tell whether the run originated from a webhook, a
/// chat plan, or the scheduler. Scoped — one orchestration context per
/// dispatch cycle.
/// </summary>
/// <param name="db">Orchestration context of the current scope.</param>
/// <param name="defaults">Worker image / profiles-ref every scheduled run claims on.</param>
/// <param name="clock">Wall-clock source for the run stamps.</param>
public sealed class SchedulerRunLauncher(
    OrchestrationDbContext db,
    IOptions<SchedulerWorkerDefaults> defaults,
    TimeProvider clock) : ISchedulerDispatcher
{
    /// <inheritdoc />
    public async Task<RunId> DispatchAsync(ScheduledJob job, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var run = Run.Create(job.ProjectId, now);
        var workItem = WorkItem.Create(
            run.Id,
            job.ProfileKey,
            defaults.Value.Image,
            defaults.Value.ProfilesRef,
            job.BriefJson,
            WorkItemStatus.Queued,
            now);

        db.Runs.Add(run);
        db.WorkItems.Add(workItem);
        await db.SaveChangesAsync(cancellationToken);
        return run.Id;
    }
}
