using Comuki.Host.Integration.Models;
using Comuki.Modules.Identity.Application.Permissions;
using Comuki.Modules.Integrations.Application.Tickets;
using Comuki.Modules.Integrations.Application.Views;
using Comuki.Shared.Kernel.Ids;
using Microsoft.AspNetCore.Mvc;

namespace Comuki.Host.Integration.Controllers;

/// <summary>
/// The native integrations surface: creates an inbound item AND its run
/// in one motion — no admission rules, no sync-back (there is no
/// external tracker). Subject to the same one-live-run lock as tracker
/// inbound items.
/// </summary>
[ApiController]
[Route(ApiRoutes.Items)]
[RequiresPermission("run:create")]
public sealed class ItemsController(CreateNativeInboundItemHandler nativeInboundItems) : ControllerBase
{
    /// <summary>Creates a native inbound item and launches its run.</summary>
    /// <param name="request"></param>
    /// <param name="cancellationToken"></param>
    [HttpPost]
    [ProducesResponseType<InboundItemView>(StatusCodes.Status201Created)]
    public async Task<ActionResult> CreateAsync(CreateNativeInboundItemRequest request, CancellationToken cancellationToken = default)
    {
        var view = await nativeInboundItems.HandleAsync(
            new CreateNativeInboundItemCommand(
                new ProjectId(request.ProjectId),
                request.Title,
                request.Body,
                request.ExternalId,
                request.Author),
            cancellationToken);

        return new CreatedResult(ApiRoutes.Items + "/" + view.Id, view);
    }
}
