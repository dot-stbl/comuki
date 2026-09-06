using Comuki.Modules.Scheduler.Application.Options;
using Comuki.Modules.Scheduler.Application.Ports;
using Comuki.Modules.Scheduler.Application.Views;
using Comuki.Modules.Scheduler.Domain.Jobs;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comuki.Modules.Scheduler.Application.Jobs;

/// <summary>
/// Service-layer façade over <see cref="IScheduledJobStore"/>: owns
/// validation, the create / update / delete / get / list paths, and the
/// domain invariants the store alone can't enforce (e.g. cron parsing).
/// Singleton — stateless; every method opens its own scope through the
/// injected store.
/// </summary>
/// <param name="store">Persistence port (scoped — DbContext per call).</param>
/// <param name="clock">Wall-clock for domain stamps.</param>
/// <param name="validator">Structural validator for the create command.</param>
/// <param name="updateValidator">Structural validator for the patch command.</param>
/// <param name="logger">Structured logger.</param>
public sealed class ScheduledJobService(
    IScheduledJobStore store,
    TimeProvider clock,
    IValidator<CreateScheduledJobCommand> validator,
    IValidator<UpdateScheduledJobCommand> updateValidator,
    ILogger<ScheduledJobService> logger)
{
    /// <summary>Lists all jobs of a project, newest first.</summary>
    /// <param name="projectId"></param>
    /// <param name="cancellationToken"></param>
    public async Task<IReadOnlyList<ScheduledJobView>> ListAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var jobs = await store.ListAsync(new Shared.Kernel.Ids.ProjectId(projectId), cancellationToken);
        return ScheduledJobView.OfAll(jobs);
    }

    /// <summary>Reads one job.</summary>
    /// <param name="jobId"></param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="ScheduledJobNotFoundException">No job with that id.</exception>
    public async Task<ScheduledJobView> GetAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var job = await store.FindAsync(new Domain.Ids.ScheduledJobId(jobId), cancellationToken)
            ?? throw new ScheduledJobNotFoundException(new Domain.Ids.ScheduledJobId(jobId));

        return ScheduledJobView.Of(job);
    }

    /// <summary>Creates a job.</summary>
    /// <param name="command"></param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="InvalidCronExpressionException">Cron expression didn't parse.</exception>
    public async Task<ScheduledJobView> CreateAsync(
        CreateScheduledJobCommand command,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var job = ScheduledJob.Create(
            command.ProjectId,
            command.CronExpression,
            command.ProfileKey,
            command.BriefJson,
            command.RunOnOnceAt,
            command.Enabled ?? true,
            clock.GetUtcNow());

        await store.AddAsync(job, cancellationToken);
        logger.LogInformation(
            "Scheduled job {JobId} created for project {ProjectId} (profile={ProfileKey}, cron={CronExpression}, enabled={Enabled})",
            job.Id.Value, job.ProjectId.Value, job.ProfileKey, job.CronExpression, job.Enabled);

        return ScheduledJobView.Of(job);
    }

    /// <summary>Patches a job. Disabled flag + cron + profile key are the only writable knobs.</summary>
    /// <param name="command"></param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="ScheduledJobNotFoundException">No job with that id.</exception>
    /// <exception cref="InvalidCronExpressionException">Cron expression didn't parse.</exception>
    public async Task<ScheduledJobView> UpdateAsync(
        UpdateScheduledJobCommand command,
        CancellationToken cancellationToken = default)
    {
        await updateValidator.ValidateAndThrowAsync(command, cancellationToken);

        var job = await store.FindAsync(command.JobId, cancellationToken)
            ?? throw new ScheduledJobNotFoundException(command.JobId);

        job.Update(command.CronExpression, command.ProfileKey, command.Enabled, clock.GetUtcNow());
        await store.UpdateAsync(job, cancellationToken);

        logger.LogInformation(
            "Scheduled job {JobId} patched (enabled={Enabled}, cron={CronExpression})",
            job.Id.Value, job.Enabled, job.CronExpression);

        return ScheduledJobView.Of(job);
    }

    /// <summary>Deletes a job (idempotent — missing ids are a no-op).</summary>
    /// <param name="jobId"></param>
    /// <param name="cancellationToken"></param>
    public async Task DeleteAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        await store.DeleteAsync(new Domain.Ids.ScheduledJobId(jobId), cancellationToken);
        logger.LogInformation("Scheduled job {JobId} deleted", jobId);
    }
}

/// <summary>
/// Sentinel typed options accessor — the host binds
/// <see cref="SchedulerOptions"/> once and the dispatcher reads it through
/// <see cref="IOptions{TOptions}"/>; we re-export the section name so the
/// host composition is single-source.
/// </summary>
public static class SchedulerOptionsMarker
{
    /// <inheritdoc cref="SchedulerOptions.SectionName" />
    public const string SectionName = SchedulerOptions.SectionName;
}
