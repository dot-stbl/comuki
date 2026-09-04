using Comuki.Host.Runs.Models;
using Comuki.Host.Security.RateLimit;
using Comuki.Modules.Identity.Application.Permissions;
using Comuki.Shared.Contracts.Runs;
using Comuki.Shared.Filtering.Parser;
using Comuki.Shared.Filtering.Ports;
using Comuki.Shared.Kernel.Ids;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Comuki.Host.Runs.Controllers;

/// <summary>
/// Run listing surface: paged, filterable and sortable through the filter DSL
/// (grammar on <see cref="FilterQuery"/>), plus the two operator decision
/// endpoints (approve / cancel). Subject-scope filtered by the orchestration
/// context query filters — out-of-scope rows are absent, not 403.
/// </summary>
/// <param name="runs">List handler behind <c>GET /api/v1/runs</c>.</param>
/// <param name="approve">Host-side approve port (issue #S5).</param>
/// <param name="cancel">Host-side cancel port (issue #S5).</param>
[ApiController]
[Route(ApiRoutes.Runs)]
[RequiresPermission("run:read")]
public sealed class RunsController(
    RunsListHandler runs,
    IApproveRunPort approve,
    ICancelRunPort cancel) : ControllerBase
{
    /// <summary>
    /// Lists runs with optional <c>filter</c> and <c>sort</c> DSL expressions
    /// (e.g. <c>status==running</c>, <c>createdAt&gt;=now(-7d)</c>,
    /// <c>sort=updatedAt,desc</c>). Filterable fields: <c>Status</c>
    /// (eq/in/notIn), <c>CreatedAt</c> + <c>UpdatedAt</c> (range, now()).
    /// </summary>
    /// <param name="query"></param>
    /// <param name="cancellationToken"></param>
    [HttpGet]
    [ProducesResponseType<RunsPage>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public Task<ActionResult> ListAsync([FromQuery] FilterQuery query, CancellationToken cancellationToken = default)
    {
        return RunsEndpointRunner.ExecuteAsync(
            async () => new OkObjectResult(await runs.ListAsync(query, cancellationToken)));
    }

    /// <summary>
    /// Approves a run that the orchestrator escalated back to a human gate.
    /// The transition is legal from <c>Escalated</c> only; every other
    /// source (including terminal statuses) answers 409. Successful approve
    /// returns 204 with no body; the orchestrator's journal
    /// (<c>run.status_changed</c>) records the transition.
    /// </summary>
    /// <param name="runId">Run to approve.</param>
    /// <param name="cancellationToken"></param>
    [HttpPost("{runId:guid}/approve")]
    [EnableRateLimiting(RateLimitPolicies.RunDecision)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult> ApproveAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        return RunsEndpointRunner.ExecuteAsync(async () =>
        {
            await approve.ApproveAsync(new RunId(runId), cancellationToken);
            return new StatusCodeResult(StatusCodes.Status204NoContent);
        });
    }

    /// <summary>
    /// Cancels a run that's still in flight. Legal from <c>Queued</c>,
    /// <c>Waiting</c>, <c>Running</c>, <c>Escalated</c>; terminal runs
    /// (<c>Succeeded</c>, <c>Cancelled</c>) answer 409. When <c>reason</c>
    /// is supplied it is journalled as a <c>reason</c> field on the
    /// <c>run.status_changed</c> event. Successful cancel returns 204.
    /// </summary>
    /// <param name="runId">Run to cancel.</param>
    /// <param name="request">Cancel body — carries optional <c>reason</c>.</param>
    /// <param name="cancellationToken"></param>
    [HttpPost("{runId:guid}/cancel")]
    [EnableRateLimiting(RateLimitPolicies.RunDecision)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult> CancelAsync(
        Guid runId,
        [FromBody] CancelRunRequest request,
        CancellationToken cancellationToken = default)
    {
        return RunsEndpointRunner.ExecuteAsync(async () =>
        {
            await cancel.CancelAsync(new RunId(runId), request.Reason, cancellationToken);
            return new StatusCodeResult(StatusCodes.Status204NoContent);
        });
    }
}

/// <summary>Typed exceptions → ProblemDetails — one place for the runs surface.</summary>
public static class RunsEndpointRunner
{
    /// <summary>Runs one endpoint body, mapping the filter DSL's parse failures + run-state conflicts.</summary>
    /// <param name="action">Endpoint body.</param>
    public static async Task<ActionResult> ExecuteAsync(Func<Task<ActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (FilterParseException exception)
        {
            return RunsProblems.InvalidFilter(exception.Message);
        }
        catch (RunDecisionConflictException exception)
        {
            return RunsProblems.StateConflict(exception);
        }
    }
}

/// <summary>Problem results of the runs surface (same shape as the chat/auth surfaces).</summary>
public static class RunsProblems
{
    /// <summary>400 for an unparseable or illegal filter/sort expression.</summary>
    /// <param name="detail"></param>
    public static ActionResult InvalidFilter(string detail)
    {
        // Build with TypedResults.Problem so the title/type defaults and
        // extension shape stay canonical (issue #20), then wrap in
        // ObjectResult for the controller-side ActionResult contract.
        var typed = TypedResults.Problem(
            title: "Invalid filter expression",
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?> { ["code"] = "filter.invalid" });

        return new ObjectResult(typed.ProblemDetails)
        {
            StatusCode = typed.StatusCode,
            ContentTypes = { "application/problem+json" },
        };
    }

    /// <summary>409 for a decision endpoint called on a run whose current status is the wrong source.</summary>
    /// <param name="exception">The typed conflict raised by the host adapter.</param>
    public static ActionResult StateConflict(RunDecisionConflictException exception)
    {
        var typed = TypedResults.Problem(
            title: "Run state conflict",
            detail: exception.Message,
            statusCode: StatusCodes.Status409Conflict,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "run.terminal_state",
                ["currentStatus"] = exception.Current.ToString(),
                ["decision"] = exception.Decision,
            });

        return new ObjectResult(typed.ProblemDetails)
        {
            StatusCode = typed.StatusCode,
            ContentTypes = { "application/problem+json" },
        };
    }
}
