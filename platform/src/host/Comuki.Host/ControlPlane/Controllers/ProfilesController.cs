using Comuki.Modules.Identity.Application.Permissions;
using Comuki.Shared.Contracts.ControlPlane.Profiles;
using Microsoft.AspNetCore.Mvc;

namespace Comuki.Host.ControlPlane.Controllers;

/// <summary>
/// Worker-profile reads from the control-plane catalog. Demands
/// <c>plan:read</c> — the enforcement filter answers 401 for anonymous
/// callers and 403 <c>permission.denied</c> for subjects without the key.
/// </summary>
/// <param name="catalog"></param>
[ApiController]
[Route(ApiRoutes.Profiles)]
[RequiresPermission("plan:read")]
public sealed class ProfilesController(IProfileCatalog catalog) : ControllerBase
{
    /// <summary>Lists every worker profile.</summary>
    /// <param name="cancellationToken"></param>
    [HttpGet("")]
    [ProducesResponseType<IReadOnlyList<ProfileDefinition>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ProfileDefinition>>> ListAsync(CancellationToken cancellationToken = default)
    {
        return Ok(await catalog.ListAsync(cancellationToken));
    }

    /// <summary>Returns one worker profile by key; 404 when the key is unknown.</summary>
    /// <param name="key"></param>
    /// <param name="cancellationToken"></param>
    [HttpGet("{key}")]
    [ProducesResponseType<ProfileDefinition>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        return await catalog.GetAsync(key, cancellationToken) is { } profile
            ? Ok(profile)
            : NotFound();
    }
}
