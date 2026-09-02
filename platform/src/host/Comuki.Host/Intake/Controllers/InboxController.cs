using Comuki.Host.Intake.Models;
using Comuki.Modules.Identity.Application.Permissions;
using Comuki.Modules.Intake.Application.Inbox;
using Comuki.Modules.Intake.Application.Tickets;
using Comuki.Modules.Intake.Application.Views;
using Comuki.Modules.Intake.Domain.Ids;
using Comuki.Shared.Kernel.Ids;
using Microsoft.AspNetCore.Mvc;

namespace Comuki.Host.Intake.Controllers;

/// <summary>
/// The inbox surface: the pending list (webhook-parked tickets), the
/// live external catalog browse and the manual claim that turns a
/// pending ticket into a run — exactly once.
/// </summary>
/// <param name="inbox"></param>
/// <param name="claims"></param>
[ApiController]
[Route(ApiRoutes.Inbox)]
public sealed class InboxController(
    InboxCatalogReader inbox,
    ClaimTicketHandler claims) : ControllerBase
{
    /// <summary>Lists pending tickets (the inbox), newest first.</summary>
    /// <param name="projectId">Optional project filter.</param>
    /// <param name="cancellationToken"></param>
    [HttpGet]
    [RequiresPermission("intake:read")]
    [ProducesResponseType<IReadOnlyList<IntakeTicketView>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<IntakeTicketView>>> ListPendingAsync(
        [FromQuery] Guid? projectId,
        CancellationToken cancellationToken = default)
    {
        return Ok(await inbox.ListPendingAsync(
            projectId is { } value ? new ProjectId(value) : null,
            cancellationToken));
    }

    /// <summary>Fetches one page of a connection's external issue catalog.</summary>
    /// <param name="connectionId"></param>
    /// <param name="page">1-based page number.</param>
    /// <param name="cancellationToken"></param>
    [HttpGet("catalog")]
    [RequiresPermission("intake:read")]
    [ProducesResponseType<IReadOnlyList<IntakeTicketView>>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<ActionResult> FetchCatalogAsync(
        [FromQuery] Guid connectionId,
        [FromQuery] int page = 1,
        CancellationToken cancellationToken = default)
    {
        return IntakeEndpointRunner.ExecuteAsync(async () =>
            Ok(await inbox.FetchCatalogAsync(new SourceConnectionId(connectionId), page, cancellationToken)));
    }

    /// <summary>Claims one pending ticket into a run (exactly once — a repeat answers 409).</summary>
    /// <param name="request"></param>
    /// <param name="cancellationToken"></param>
    [HttpPost("claim")]
    [RequiresPermission("intake:claim")]
    [ProducesResponseType<IntakeTicketView>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult> ClaimAsync(ClaimTicketRequest request, CancellationToken cancellationToken = default)
    {
        return IntakeEndpointRunner.ExecuteAsync(async () =>
            Ok(await claims.HandleAsync(new ClaimTicketCommand(new IncomingTicketId(request.TicketId)), cancellationToken)));
    }
}
