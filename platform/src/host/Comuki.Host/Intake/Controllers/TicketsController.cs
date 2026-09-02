using Comuki.Host.Intake.Models;
using Comuki.Modules.Identity.Application.Permissions;
using Comuki.Modules.Intake.Application.Tickets;
using Comuki.Modules.Intake.Application.Views;
using Comuki.Shared.Kernel.Ids;
using Microsoft.AspNetCore.Mvc;

namespace Comuki.Host.Intake.Controllers;

/// <summary>
/// The native intake surface: creates a ticket AND its run in one
/// motion — no admission rules, no sync-back (there is no external
/// tracker). Subject to the same one-live-run lock as tracker tickets.
/// </summary>
/// <param name="nativeTickets"></param>
[ApiController]
[Route(ApiRoutes.Tickets)]
[RequiresPermission("run:create")]
public sealed class TicketsController(CreateNativeTicketHandler nativeTickets) : ControllerBase
{
    /// <summary>Creates a native ticket and launches its run.</summary>
    /// <param name="request"></param>
    /// <param name="cancellationToken"></param>
    [HttpPost]
    [ProducesResponseType<IntakeTicketView>(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult> CreateAsync(CreateNativeTicketRequest request, CancellationToken cancellationToken = default)
    {
        return IntakeEndpointRunner.ExecuteAsync(async () =>
        {
            var view = await nativeTickets.HandleAsync(
                new CreateNativeTicketCommand(
                    new ProjectId(request.ProjectId),
                    request.Title,
                    request.Body,
                    request.ExternalId,
                    request.Author),
                cancellationToken);

            return new CreatedResult(ApiRoutes.Tickets + "/" + view.Id, view);
        });
    }
}
