using Comuki.Engine.Compute.Options;
using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Domain.WorkItems;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Host.Projects;
using Comuki.Modules.Projects.Application.Ports;
using Comuki.Modules.Scheduler.Application.Ports;
using Comuki.Modules.Scheduler.Domain.Jobs;
using Comuki.Shared.Bootstrap.Versioning;
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
/// <param name="buildInformation">Build identity — pins the item image to the running version (release contract).</param>
/// <param name="clock">Wall-clock source for the run stamps.</param>
/// <param name="projects">Projects module port — stamps <c>Project.EnvClass</c> (task 3.1).</param>
public sealed class SchedulerRunLauncher(
    OrchestrationDbContext db,
    IOptions<SchedulerWorkerDefaults> defaults,
    ComukiBuildInformation buildInformation,
    TimeProvider clock,
    IProjectStore projects) : ISchedulerDispatcher
{
    /// <inheritdoc />
    public async Task<RunId> DispatchAsync(ScheduledJob job, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var run = Run.Create(job.ProjectId, now);
        // Claim matching compares the item's image with the worker's
        // labels for equality — the supervisor pins its spawn through
        // WorkerImagePinning, so the item side must resolve through the
        // same function or no worker ever matches (release contract,
        // see WorkerImagePinning).
        var image = WorkerImagePinning.Resolve(defaults.Value.Image, buildInformation);
        var envClass = await EnvClassResolver.ResolveAsync(projects, job.ProjectId, "scheduler dispatch", cancellationToken);
        var workItem = WorkItem.Create(
            run.Id,
            job.ProfileKey,
            image,
            envClass,
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
