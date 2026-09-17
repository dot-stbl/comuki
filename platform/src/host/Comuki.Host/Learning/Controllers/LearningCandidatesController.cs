using Comuki.Host.Learning.Models;
using Comuki.Modules.Identity.Application.Permissions;
using Comuki.Modules.Memory.Application.Learning;
using Comuki.Modules.Memory.Application.Views;
using Comuki.Modules.Memory.Domain.Ids;
using Comuki.Modules.Memory.Domain.Learning;
using Microsoft.AspNetCore.Mvc;

namespace Comuki.Host.Learning.Controllers;

/// <summary>
/// The approvals surface of the learning loop: the candidate queue a human
/// works through (pending rule suggestions workers queued via
/// <c>learning.suggest</c>), and the two decisions. Approve publishes the
/// rule (v1: a standing memory fact the brain and workers find via
/// memory.recall); reject keeps the row for history and never publishes.
/// </summary>
/// <param name="candidates">Candidate store behind the list read.</param>
/// <param name="decisions">Approve/reject coordination (decision + rule publication).</param>
[ApiController]
[Route(ApiRoutes.LearningCandidates)]
[RequiresPermission("learning:read")]
public sealed class LearningCandidatesController(
    ILearningCandidateStore candidates,
    LearningApprovalService decisions) : ControllerBase
{
    /// <summary>
    /// Lists learning candidates, newest first. <c>status</c> takes a wire
    /// key (<c>pending</c> | <c>approved</c> | <c>rejected</c>); an unknown
    /// key answers 400, omitting it lists every state.
    /// </summary>
    /// <param name="status">Optional status wire key.</param>
    /// <param name="cancellationToken"></param>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<LearningCandidateView>>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public Task<ActionResult> ListAsync([FromQuery] string? status, CancellationToken cancellationToken = default)
    {
        return LearningEndpointRunner.ExecuteAsync(async () =>
        {
            var parsed = string.IsNullOrWhiteSpace(status) ? null : LearningStatusKeys.Parse(status);
            return parsed is null && !string.IsNullOrWhiteSpace(status)
                ? LearningProblems.InvalidStatus(status)
                : new OkObjectResult(await candidates.ListAsync(parsed, cancellationToken: cancellationToken));
        });
    }

    /// <summary>
    /// Reads one learning candidate. Absent ids read as 404.
    /// </summary>
    /// <param name="candidateId">Candidate to read.</param>
    /// <param name="cancellationToken"></param>
    [HttpGet("{candidateId:guid}", Name = "learning-candidates-get-by-id")]
    [ProducesResponseType<LearningCandidateView>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<ActionResult> GetAsync(Guid candidateId, CancellationToken cancellationToken = default)
    {
        return LearningEndpointRunner.ExecuteAsync(async () =>
            await candidates.GetAsync(new LearningCandidateId(candidateId), cancellationToken) is { } candidate
                ? new OkObjectResult(candidate)
                : LearningProblems.CandidateNotFound(candidateId));
    }

    /// <summary>
    /// Approves a pending candidate and publishes its rule. Successful
    /// approve returns 204; deciding an already-decided candidate answers
    /// 409 (an already-approved one is idempotent — the retry after a
    /// publish failure re-publishes rather than conflicts).
    /// </summary>
    /// <param name="candidateId">Candidate to approve.</param>
    /// <param name="cancellationToken"></param>
    [HttpPost("{candidateId:guid}/approve")]
    [RequiresPermission("learning:write")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult> ApproveAsync(Guid candidateId, CancellationToken cancellationToken = default)
    {
        return LearningEndpointRunner.ExecuteAsync(async () =>
            await decisions.ApproveAsync(new LearningCandidateId(candidateId), cancellationToken) is null
                ? LearningProblems.CandidateNotFound(candidateId)
                : new StatusCodeResult(StatusCodes.Status204NoContent));
    }

    /// <summary>
    /// Rejects a pending candidate with an optional reason (journalled on
    /// the row). A rejected rule is never published. Successful reject
    /// returns 204; deciding twice answers 409.
    /// </summary>
    /// <param name="candidateId">Candidate to reject.</param>
    /// <param name="request">Reject body — carries optional <c>reason</c>.</param>
    /// <param name="cancellationToken"></param>
    [HttpPost("{candidateId:guid}/reject")]
    [RequiresPermission("learning:write")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult> RejectAsync(
        Guid candidateId,
        [FromBody] RejectLearningCandidateRequest request,
        CancellationToken cancellationToken = default)
    {
        return LearningEndpointRunner.ExecuteAsync(async () =>
            await decisions.RejectAsync(new LearningCandidateId(candidateId), request.Reason, cancellationToken) is null
                ? LearningProblems.CandidateNotFound(candidateId)
                : new StatusCodeResult(StatusCodes.Status204NoContent));
    }
}

/// <summary>Typed exceptions → ProblemDetails — one place for the learning surface.</summary>
public static class LearningEndpointRunner
{
    /// <summary>Runs one endpoint body, mapping the learning surface's typed failures.</summary>
    /// <param name="action">Endpoint body.</param>
    public static async Task<ActionResult> ExecuteAsync(Func<Task<ActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (LearningDecisionConflictException exception)
        {
            return LearningProblems.StateConflict(exception);
        }
    }
}

/// <summary>Problem results of the learning surface (same shape as the runs surface).</summary>
public static class LearningProblems
{
    /// <summary>400 for an unknown status wire key.</summary>
    /// <param name="status">The unrecognized status key.</param>
    public static ActionResult InvalidStatus(string status)
    {
        var typed = TypedResults.Problem(
            title: "Invalid status filter",
            detail: $"unknown learning status '{status}' — expected pending, approved or rejected",
            statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?> { ["code"] = "learning.invalid_status" });

        return new ObjectResult(typed.ProblemDetails)
        {
            StatusCode = typed.StatusCode,
            ContentTypes = { "application/problem+json" },
        };
    }

    /// <summary>404 for a candidate the surface cannot find.</summary>
    /// <param name="candidateId">Candidate that was looked up.</param>
    public static ActionResult CandidateNotFound(Guid candidateId)
    {
        var typed = TypedResults.Problem(
            title: "Learning candidate not found",
            detail: $"learning candidate '{candidateId}' not found",
            statusCode: StatusCodes.Status404NotFound,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "learning.candidate_not_found",
                ["candidateId"] = candidateId.ToString(),
            });

        return new ObjectResult(typed.ProblemDetails)
        {
            StatusCode = typed.StatusCode,
            ContentTypes = { "application/problem+json" },
        };
    }

    /// <summary>409 for a decision on a candidate whose current status is the wrong source.</summary>
    /// <param name="exception">The typed conflict raised by the approval service.</param>
    public static ActionResult StateConflict(LearningDecisionConflictException exception)
    {
        var typed = TypedResults.Problem(
            title: "Learning candidate already decided",
            detail: exception.Message,
            statusCode: StatusCodes.Status409Conflict,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "learning.already_decided",
                ["candidateId"] = exception.CandidateId.Value.ToString(),
                ["currentStatus"] = LearningStatusKeys.Key(exception.Current),
            });

        return new ObjectResult(typed.ProblemDetails)
        {
            StatusCode = typed.StatusCode,
            ContentTypes = { "application/problem+json" },
        };
    }
}
