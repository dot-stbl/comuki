using Comuki.Modules.Identity.Application.Permissions;
using Comuki.Shared.Contracts.ControlPlane.ChatCommands;
using Microsoft.AspNetCore.Mvc;

namespace Comuki.Host.ControlPlane.Controllers;

/// <summary>
/// Built-in chat-command pack reads. Demands <c>chat:use</c> — the
/// enforcement filter answers 401/403 like every other permission.
/// </summary>
/// <param name="catalog"></param>
[ApiController]
[Route(ApiRoutes.ChatCommands)]
[RequiresPermission("chat:use")]
public sealed class ChatCommandsController(IChatCommandCatalog catalog) : ControllerBase
{
    /// <summary>Lists every built-in chat command.</summary>
    /// <param name="cancellationToken"></param>
    [HttpGet("")]
    [ProducesResponseType<IReadOnlyList<ChatCommandDefinition>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ChatCommandDefinition>>> ListAsync(CancellationToken cancellationToken = default)
    {
        return Ok(await catalog.ListCommandsAsync(cancellationToken));
    }
}
