using Comuki.Modules.Identity.Application.Permissions;
using Comuki.Shared.Contracts.ControlPlane.Skills;
using Microsoft.AspNetCore.Mvc;

namespace Comuki.Host.ControlPlane.Controllers;

/// <summary>
/// Control-plane skill catalog reads. Demands <c>plan:read</c> — the
/// enforcement filter answers 401 for anonymous callers and 403
/// <c>permission.denied</c> for subjects without the key. The catalog is
/// a list - never an auto-selector (task 25.5): the body of this controller
/// reads <see cref="ISkillCatalog.ListAsync"/> end-to-end and never filters
/// by <c>trigger_when</c>; the brain does that on its own.
/// </summary>
/// <param name="catalog"></param>
[ApiController]
[Route(ApiRoutes.Skills)]
[RequiresPermission("plan:read")]
public sealed class SkillsController(ISkillCatalog catalog) : ControllerBase
{
    /// <summary>Lists every control-plane skill with its managed-asset metadata.</summary>
    /// <param name="cancellationToken"></param>
    [HttpGet("")]
    [ProducesResponseType<IReadOnlyList<SkillDefinition>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SkillDefinition>>> ListAsync(CancellationToken cancellationToken = default)
    {
        return Ok(await catalog.ListAsync(cancellationToken));
    }

    /// <summary>Returns one skill by key; 404 when the key is unknown.</summary>
    /// <param name="key"></param>
    /// <param name="cancellationToken"></param>
    [HttpGet("{key}")]
    [ProducesResponseType<SkillDefinition>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        return await catalog.GetAsync(key, cancellationToken) is { } skill
            ? Ok(skill)
            : NotFound();
    }
}
