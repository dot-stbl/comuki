using Comuki.Host.Runs.Models;
using Comuki.Host.Runs.Verification;
using Comuki.Host.Security.RateLimit;
using Comuki.Modules.Identity.Application.Permissions;
using Comuki.Shared.Contracts.Runs;
using Comuki.Shared.Editions;
using Comuki.Shared.Editions.Gating;
using Comuki.Shared.Filtering.Ports;
using Comuki.Shared.Kernel.Ids;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Comuki.Host.Runs.Controllers;

/// <summary>
/// Run listing surface: paged, filterable and sortable through the filter DSL
/// (grammar on <see cref="FilterQuery"/>), the per-run detail read, the
/// per-run verification view, and the three operator decision endpoints
/// (approve / cancel / steer). Subject-scope filtered by the orchestration
/// context query filters — out-of-scope rows are absent, not 403.
/// </summary>
/// <param name="runs">List handler behind <c>GET /api/v1/runs</c>.</param>
/// <param name="details">Detail handler behind <c>GET /api/v1/runs/{runId}</c>.</param>
/// <param name="verification">Verification view handler behind <c>GET /api/v1/runs/{runId}/verification</c> (add-orchestra §3 — Coda).</param>
/// <param name="approve">Host-side approve port (issue #S5).</param>
/// <param name="cancel">Host-side cancel port (issue #S5).</param>
/// <param name="steer">Host-side steer port (add-orchestra §1 — Baton).</param>
[ApiController]
[Route(ApiRoutes.Runs)]
[RequiresPermission("run:read")]
public sealed class RunsController(
    RunsListHandler runs,
    GetRunDetailHandler details,
    GetRunVerificationViewHandler verification,
    IApproveRunPort approve,
    ICancelRunPort cancel,
    ISteerRunPort steer) : ControllerBase
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
    public async Task<ActionResult> ListAsync([FromQuery] FilterQuery query, CancellationToken cancellationToken = default)
    {
        return new OkObjectResult(await runs.ListAsync(query, cancellationToken));
    }

    /// <summary>
    /// Reads the full detail of one run: work items with their DAG edges, the
    /// recent journal (top 20), the pinned revisions, and the brief. The
    /// subject-scope query filter is the only gate beyond <c>run:read</c> —
    /// a run the subject cannot see reads as 404, not 403.
    /// </summary>
    /// <param name="runId">Run to read.</param>
    /// <param name="cancellationToken"></param>
    [HttpGet("{runId:guid}", Name = "runs-get-by-id")]
    [ProducesResponseType<RunDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult> GetAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        return await details.GetAsync(new RunId(runId), cancellationToken) is { } found
            ? new OkObjectResult(found)
            : RunsProblems.RunNotFound(new RunId(runId));
    }

    /// <summary>
    /// Reads the per-gate verification view (add-orchestra §3 — Coda).
    /// Returns the flat list of (work item, gate) verdicts the
    /// orchestrator has stamped for the run. A run with no verdicts yet
    /// (project had <c>VerifyEnabled=false</c>, or all work items still
    /// pending) reads as 200 with an empty <c>Gates</c> list — the FE
    /// renders "no gates evaluated" without a separate null path. Gated
    /// by the <c>verification</c> paid feature key — Community-tier
    /// requests answer 403 <c>edition.feature_unavailable</c>.
    /// </summary>
    /// <param name="runId">Run to read the verification view for.</param>
    /// <param name="cancellationToken"></param>
    [HttpGet("{runId:guid}/verification", Name = "runs-get-verification")]
    [RequiresFeature("verification")]
    [ProducesResponseType<RunVerificationView>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> GetVerificationAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        return await verification.GetAsync(new RunId(runId), cancellationToken) is { } view
            ? new OkObjectResult(view)
            : RunsProblems.RunNotFound(new RunId(runId));
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
    public async Task<ActionResult> ApproveAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        await approve.ApproveAsync(new RunId(runId), cancellationToken);
        return new StatusCodeResult(StatusCodes.Status204NoContent);
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
    public async Task<ActionResult> CancelAsync(
        Guid runId,
        [FromBody] CancelRunRequest request,
        CancellationToken cancellationToken = default)
    {
        await cancel.CancelAsync(new RunId(runId), request.Reason, cancellationToken);
        return new StatusCodeResult(StatusCodes.Status204NoContent);
    }

    /// <summary>
    /// Steers an in-flight run (add-orchestra §1 — Baton, Phase 1a). The
    /// handler resolves <c>runId → WorkItem → LeasedBy → WorkerId</c>
    /// through the shared <see cref="IExecutionIdResolver"/> seam and,
    /// on the no-LiveSession runtime, stages a follow-up research
    /// WorkItem on the same run. The follow-up carries the operator's
    /// <c>text</c> as its brief — a fresh worker claims the new item
    /// after the in-flight lease is fenced by cancel or reaped by the
    /// lease policy. Terminal runs answer 409 <c>run.not_running</c>.
    /// Unknown run ids answer 404 <c>run.not_found</c>. Gated by the
    /// <c>steering</c> feature key (<see cref="Features.Steering"/>) —
    /// Community-tier requests answer 403
    /// <c>edition.feature_unavailable</c> (issue #164).
    /// </summary>
    /// <param name="runId">Run to steer.</param>
    /// <param name="request">Steer body — carries the operator's text.</param>
    /// <param name="steerValidator"></param>
    /// <param name="cancellationToken"></param>
    [HttpPost("{runId:guid}/steer")]
    [RequiresFeature("steering")]
    [EnableRateLimiting(RateLimitPolicies.RunDecision)]
    [ProducesResponseType<SteerRunResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> SteerAsync(
        Guid runId,
        [FromBody] SteerRunRequest request,
        [FromServices] IValidator<SteerRunRequest> steerValidator,
        CancellationToken cancellationToken = default)
    {
        if (await RunsValidation.ValidateSteerAsync(
                steerValidator, request,
                problemTitle: "Steer text is required",
                defaultCode: "steer.text_required",
                cancellationToken) is { } problem)
        {
            return problem;
        }

        var result = await steer.SteerAsync(new RunId(runId), request.Text.Trim(), cancellationToken);

        return new ObjectResult(new SteerRunResponse(result.Delivered, result.FollowUpWorkItemId))
        {
            StatusCode = StatusCodes.Status202Accepted,
        };
    }
}

/// <summary>
/// The 404 row of the runs value flow — a run the detail handler did not
/// find is a result, not a thrown exception, so the row stays endpoint-local
/// per the domain-error-contract scope. The 400 row for the steer
/// endpoint moved to <see cref="RunsValidation"/> when the empty-text
/// check became a FluentValidation rule on <see cref="SteerRunRequest"/>.
/// </summary>
public static class RunsProblems
{
    /// <summary>404 for the detail endpoint when the run is absent or out of subject scope.</summary>
    /// <param name="runId">Run that was looked up.</param>
    public static ActionResult RunNotFound(RunId runId)
    {
        // Build with TypedResults.Problem so the title/type defaults and
        // extension shape stay canonical (issue #20), then wrap in
        // ObjectResult for the controller-side ActionResult contract.
        var typed = TypedResults.Problem(
            title: "Run not found",
            detail: $"run '{runId.Value}' not found",
            statusCode: StatusCodes.Status404NotFound,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "run.not_found",
                ["runId"] = runId.Value.ToString(),
            });

        return new ObjectResult(typed.ProblemDetails)
        {
            StatusCode = typed.StatusCode,
            ContentTypes = { "application/problem+json" },
        };
    }
}

/// <summary>
/// FluentValidation glue for the runs surface endpoints. Translates a
/// validator failure into the canonical 400 <c>application/problem+json</c>
/// shape, preserving the validator's <c>ValidationFailure.ErrorCode</c>
/// as the wire-format <c>code</c> extension so the contract declared
/// inside the validator (e.g. <c>steer.text_required</c>) survives the
/// trip through the ASP.NET pipeline.
/// </summary>
file static class RunsValidation
{
    /// <summary>
    /// Run the steer request validator; return a 400 ProblemDetails on
    /// failure or <c>null</c> on success. The first failure's
    /// <c>ErrorCode</c> (set on the rule by <c>WithErrorCode(...)</c>)
    /// becomes the <c>extensions.code</c> field — the same shape the
    /// controller-side guard produced before the rule moved into the
    /// validator.
    /// </summary>
    /// <param name="validator">FluentValidation validator for <see cref="SteerRunRequest"/>.</param>
    /// <param name="request">The steer request body to validate.</param>
    /// <param name="problemTitle">Human-readable title for the 400 envelope; typically a one-liner naming the missing field.</param>
    /// <param name="defaultCode">Wire-format <c>code</c> extension when the validator didn't tag the failure with its own <c>ErrorCode</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<ActionResult?> ValidateSteerAsync(
        IValidator<SteerRunRequest> validator,
        SteerRunRequest request,
        string problemTitle,
        string defaultCode,
        CancellationToken cancellationToken)
    {
        var result = await validator.ValidateAsync(request, cancellationToken);

        if (result.IsValid)
        {
            return null;
        }

        var failure = result.Errors[0];
        var typed = TypedResults.Problem(
            title: problemTitle,
            detail: failure.ErrorMessage,
            statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = failure.ErrorCode ?? defaultCode,
            });

        return new ObjectResult(typed.ProblemDetails)
        {
            StatusCode = typed.StatusCode,
            ContentTypes = { "application/problem+json" },
        };
    }
}
