using Comuki.Host.Scheduler.Models;
using Comuki.Modules.Identity.Application.Permissions;
using Comuki.Modules.Scheduler.Application.Jobs;
using Comuki.Modules.Scheduler.Application.Views;
using Comuki.Modules.Scheduler.Domain.Ids;
using Comuki.Shared.Kernel.Ids;
using Microsoft.AspNetCore.Mvc;

namespace Comuki.Host.Scheduler;

/// <summary>
/// Scheduled jobs CRUD — the per-project cron / one-shot admission
/// source (S15). Reads demand <c>scheduler:read</c>; writes demand
/// <c>scheduler:write</c>. Routes are constant-typed (see
/// <see cref="ApiRoutes"/>) — no inline literals in <c>[Route]</c>.
/// </summary>
/// <param name="jobs">Service façade over the scheduler store.</param>
[ApiController]
[Route(ApiRoutes.SchedulerJobs)]
public sealed class ScheduledJobsController(ScheduledJobService jobs) : ControllerBase
{
    /// <summary>Lists scheduled jobs for a project, newest first.</summary>
    /// <param name="projectId">Owning project.</param>
    /// <param name="page">1-based page index (default 1).</param>
    /// <param name="pageSize">Page size (default 100, max 500).</summary>
    /// <param name="cancellationToken"></param>
    [HttpGet]
    [RequiresPermission("scheduler:read")]
    [ProducesResponseType<ScheduledJobsPage>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ScheduledJobsPage>> ListAsync(
        [FromRoute] Guid projectId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        if (page < 1)
        {
            return SchedulerProblems.Problem(
                StatusCodes.Status400BadRequest,
                "scheduler.bad_page",
                "Invalid pagination",
                "page must be >= 1");
        }

        var clampedPageSize = Math.Clamp(pageSize, 1, 500);
        var all = await jobs.ListAsync(projectId, cancellationToken);
        var skip = (page - 1) * clampedPageSize;
        var slice = all.Skip(skip).Take(clampedPageSize).ToArray();

        return Ok(new ScheduledJobsPage(slice, all.Count));
    }

    /// <summary>Reads one scheduled job.</summary>
    /// <param name="projectId">Owning project (route context, asserted equal).</param>
    /// <param name="jobId">Job id.</param>
    /// <param name="cancellationToken"></param>
    [HttpGet("{jobId:guid}")]
    [RequiresPermission("scheduler:read")]
    [ProducesResponseType<ScheduledJobView>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<ActionResult> GetAsync(
        [FromRoute] Guid projectId,
        [FromRoute] Guid jobId,
        CancellationToken cancellationToken = default)
    {
        return SchedulerEndpointRunner.ExecuteAsync(async () =>
            Ok(await jobs.GetAsync(jobId, cancellationToken)));
    }

    /// <summary>Creates a scheduled job for a project.</summary>
    /// <param name="projectId">Owning project.</param>
    /// <param name="request">Create body.</param>
    /// <param name="cancellationToken"></param>
    [HttpPost]
    [RequiresPermission("scheduler:write")]
    [ProducesResponseType<ScheduledJobView>(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public Task<ActionResult> CreateAsync(
        [FromRoute] Guid projectId,
        [FromBody] CreateScheduledJobRequest request,
        CancellationToken cancellationToken = default)
    {
        return SchedulerEndpointRunner.ExecuteAsync(async () =>
        {
            var view = await jobs.CreateAsync(
                new CreateScheduledJobCommand(
                    new ProjectId(projectId),
                    request.CronExpression,
                    request.ProfileKey,
                    request.BriefJson,
                    request.RunOnOnceAt,
                    request.Enabled),
                cancellationToken);

            // Class-level route is "/api/v1/projects/{projectId:guid}/scheduled-jobs";
            // the location header is the constant with the placeholder
            // substituted for the request's projectId — no inline literal.
            var location = ApiRoutes.SchedulerJobs + "/" + view.Id;
            return new CreatedResult(location.Replace("{projectId:guid}", projectId.ToString()), view);
        });
    }

    /// <summary>Partial update — null fields leave the stored value untouched.</summary>
    /// <param name="projectId">Owning project (route context).</param>
    /// <param name="jobId">Job id.</param>
    /// <param name="request">Patch body.</param>
    /// <param name="cancellationToken"></param>
    [HttpPatch("{jobId:guid}")]
    [RequiresPermission("scheduler:write")]
    [ProducesResponseType<ScheduledJobView>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<ActionResult> UpdateAsync(
        [FromRoute] Guid projectId,
        [FromRoute] Guid jobId,
        [FromBody] UpdateScheduledJobRequest request,
        CancellationToken cancellationToken = default)
    {
        return SchedulerEndpointRunner.ExecuteAsync(async () =>
            Ok(await jobs.UpdateAsync(
                new UpdateScheduledJobCommand(
                    new ScheduledJobId(jobId),
                    request.CronExpression,
                    null,
                    request.Enabled),
                cancellationToken)));
    }

    /// <summary>Deletes a scheduled job (idempotent — missing ids are a no-op).</summary>
    /// <param name="projectId">Owning project (route context).</param>
    /// <param name="jobId">Job id.</param>
    /// <param name="cancellationToken"></param>
    [HttpDelete("{jobId:guid}")]
    [RequiresPermission("scheduler:write")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteAsync(
        [FromRoute] Guid projectId,
        [FromRoute] Guid jobId,
        CancellationToken cancellationToken = default)
    {
        await jobs.DeleteAsync(jobId, cancellationToken);
        return NoContent();
    }
}
