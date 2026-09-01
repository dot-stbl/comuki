using Comuki.Modules.Chat.Application.Slash;
using Comuki.Modules.Identity.Application.Permissions;
using Microsoft.AspNetCore.Mvc;

namespace Comuki.Host.Chat.Controllers;

/// <summary>
/// The merged slash-command catalog for autocomplete: graph-native
/// built-ins plus the control-plane <c>chat-commands/</c> pack. Demands
/// <c>chat:use</c>; invoking a command happens by posting <c>/key args</c>
/// as a normal message turn.
/// </summary>
/// <param name="slashCatalog">Merged catalog.</param>
[ApiController]
[Route(ApiRoutes.ChatSlash)]
[RequiresPermission("chat:use")]
public sealed class ChatSlashController(ChatSlashCatalog slashCatalog) : ControllerBase
{
    /// <summary>Lists every available slash command, ordered by key.</summary>
    /// <param name="cancellationToken"></param>
    [HttpGet("")]
    [ProducesResponseType<IReadOnlyList<ChatSlashCommand>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ChatSlashCommand>>> ListAsync(CancellationToken cancellationToken = default)
    {
        return Ok(await slashCatalog.ListAsync(cancellationToken));
    }
}
