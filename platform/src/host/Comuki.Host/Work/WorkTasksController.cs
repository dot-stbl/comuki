using Comuki.Modules.Identity.Application.Permissions;
using Comuki.Modules.Identity.Domain.Permissions;
using Comuki.Modules.Work.Application.Ports.Capabilities;
using Comuki.Modules.Work.Application.Ports.Persistence;
using Comuki.Modules.Work.Domain.Ids;
using Microsoft.AspNetCore.Mvc;

namespace Comuki.Host.Work;

/// <summary>
/// WorkTask read surface — the umbrella's <c>add-work-management</c>
/// design §"Work API". Every endpoint carries the
/// <c>work.read</c> permission (see <see cref="Permissions"/>); the
/// per-row read at <see cref="ApiRoutes.WorkTaskById"/> is the
/// Run-shaped compatibility projection (per the design §"Run-consumer
/// compatibility projection") and returns 404 when the requested
/// attempt has been fenced by a replacement.
/// <para>
/// The endpoint group is mounted under
/// <see cref="ApiRoutes.WorkTasks"/>; the capabilities probe at
/// <see cref="ApiRoutes.WorkCapabilities"/> surfaces the
/// <see cref="WorkCapabilities"/> catalogue for the Capability Broker
/// (task 3.7).
/// </para>
/// </summary>
[ApiController]
[Route(ApiRoutes.WorkTasks)]
[RequiresPermission("work:read")]
public sealed class WorkTasksController(IWorkTaskStore store) : ControllerBase
{
    /// <summary>
    /// Returns one WorkTask by id. 404 when the Task does not exist or
    /// is out of scope for the requesting subject. The response
    /// shape is the per-Task detail projection — caller-facing fields
    /// only (no attempt ledger internals).
    /// </summary>
    [HttpGet("{taskId:guid}")]
    [EndpointName("work-tasks-get")]
    [ProducesResponseType<WorkTaskDetailView>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WorkTaskDetailView>> GetAsync(
        Guid taskId,
        CancellationToken cancellationToken = default)
    {
        var id = new WorkTaskId(taskId);
        var task = await store.FindAsync(id, cancellationToken);
        return task is null
            ? NotFound()
            : Ok(WorkTaskDetailView.From(task));
    }

    /// <summary>
    /// Run-shaped projection of the WorkTask's current attempt — the
    /// compatibility surface for the transition from Run-shaped to
    /// WorkTask-shaped state (per the umbrella's
    /// <c>add-work-management</c> design §"Run-consumer compatibility
    /// projection"). The response shape is shape-compatible with the
    /// pre-<c>WorkTask</c> <c>GET /api/v1/runs/{runId}</c> response and
    /// includes the authoritative <c>taskId</c> field. 404 when the
    /// requested attempt has been fenced by a replacement.
    /// </summary>
    [HttpGet("{taskId:guid}/run-view")]
    [EndpointName("work-tasks-run-view")]
    [ProducesResponseType<WorkTaskRunView>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WorkTaskRunView>> RunViewAsync(
        Guid taskId,
        CancellationToken cancellationToken = default)
    {
        var id = new WorkTaskId(taskId);
        var task = await store.FindAsync(id, cancellationToken);
        if (task is null)
        {
            return NotFound();
        }

        // The compatibility projection only renders Tasks with a
        // currently-active attempt — Tasks without one (Draft /
        // Ready / Blocked / terminal-but-not-attached) have no Run
        // surface to project onto; callers should read the per-Task
        // detail for those.
        return task.ActiveAttemptId is { Value: { } activeRunIdGuid }
            ? Ok(WorkTaskRunView.From(task, activeRunIdGuid))
            : NotFound();
    }
}

/// <summary>
/// Capability surface for the Work module — the static catalogue
/// surfaced to the Capability Broker (the umbrella's task 3.7 / #90).
/// Mounted at <c>/api/v1/work/capabilities</c>; read-only,
/// permission work:read.
/// </summary>
[ApiController]
[Route(ApiRoutes.WorkCapabilities)]
[RequiresPermission("work:read")]
public sealed class WorkCapabilitiesController : ControllerBase
{
    /// <summary>
    /// The full set of <see cref="WorkCapabilities"/> the Work module
    /// exposes. Stable contract; the worker's response is the wire
    /// form the Broker indexes (see
    /// <c>openspec/changes/add-mission-cowork/design.md</c>).
    /// </summary>
    [HttpGet]
    [EndpointName("work-capabilities-list")]
    [ProducesResponseType<WorkCapabilitiesView>(StatusCodes.Status200OK)]
    public async Task<ActionResult<WorkCapabilitiesView>> ListAsync()
    {
        return await Task.FromResult<ActionResult<WorkCapabilitiesView>>(Ok(WorkCapabilitiesView.Create()));
    }
}
