using Comuki.Host.Intake.Models;
using Comuki.Modules.Identity.Application.Permissions;
using Comuki.Modules.Intake.Application.Sources;
using Comuki.Modules.Intake.Application.Views;
using Comuki.Modules.Intake.Domain.Ids;
using Comuki.Shared.Kernel.Ids;
using Microsoft.AspNetCore.Mvc;

namespace Comuki.Host.Intake.Controllers;

/// <summary>
/// Source connection CRUD: the tracker bindings with their webhook
/// routing keys and env-ref secrets. Reads demand <c>intake:read</c>;
/// writes demand <c>source:write</c>.
/// </summary>
/// <param name="connections"></param>
[ApiController]
[Route(ApiRoutes.Sources)]
public sealed class SourcesController(SourceConnectionService connections) : ControllerBase
{
    /// <summary>Lists connections, optionally per project.</summary>
    /// <param name="projectId">Optional project filter.</param>
    /// <param name="cancellationToken"></param>
    [HttpGet]
    [RequiresPermission("intake:read")]
    [ProducesResponseType<IReadOnlyList<SourceConnectionView>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SourceConnectionView>>> ListAsync(
        [FromQuery] Guid? projectId,
        CancellationToken cancellationToken = default)
    {
        return Ok(await connections.ListAsync(
            projectId is { } value ? new ProjectId(value) : null,
            cancellationToken));
    }

    /// <summary>Creates a connection; the view carries the webhook path to configure in the tracker.</summary>
    /// <param name="request"></param>
    /// <param name="cancellationToken"></param>
    [HttpPost]
    [RequiresPermission("source:write")]
    [ProducesResponseType<SourceConnectionView>(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public Task<ActionResult> CreateAsync(CreateSourceConnectionRequest request, CancellationToken cancellationToken = default)
    {
        return IntakeEndpointRunner.ExecuteAsync(async () =>
        {
            var view = await connections.CreateAsync(
                new CreateSourceConnectionCommand(
                    new ProjectId(request.ProjectId),
                    request.Provider,
                    request.Name,
                    request.SettingsJson,
                    request.SecretEnvRef),
                cancellationToken);

            return new CreatedResult(ApiRoutes.Sources + "/" + view.Id, view);
        });
    }

    /// <summary>Reads one connection.</summary>
    /// <param name="sourceId"></param>
    /// <param name="cancellationToken"></param>
    [HttpGet("{sourceId:guid}")]
    [RequiresPermission("intake:read")]
    [ProducesResponseType<SourceConnectionView>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<ActionResult> GetAsync(Guid sourceId, CancellationToken cancellationToken = default)
    {
        return IntakeEndpointRunner.ExecuteAsync(async () =>
            Ok(await connections.GetAsync(new SourceConnectionId(sourceId), cancellationToken)));
    }

    /// <summary>Partial update (PATCH semantics — null fields stay).</summary>
    /// <param name="sourceId"></param>
    /// <param name="request"></param>
    /// <param name="cancellationToken"></param>
    [HttpPut("{sourceId:guid}")]
    [RequiresPermission("source:write")]
    [ProducesResponseType<SourceConnectionView>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<ActionResult> UpdateAsync(Guid sourceId, UpdateSourceConnectionRequest request, CancellationToken cancellationToken = default)
    {
        return IntakeEndpointRunner.ExecuteAsync(async () =>
            Ok(await connections.UpdateAsync(
                new SourceConnectionId(sourceId),
                request.Name,
                request.SettingsJson,
                request.SecretEnvRef,
                request.Enabled,
                cancellationToken)));
    }

    /// <summary>Deletes a connection (idempotent).</summary>
    /// <param name="sourceId"></param>
    /// <param name="cancellationToken"></param>
    [HttpDelete("{sourceId:guid}")]
    [RequiresPermission("source:write")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteAsync(Guid sourceId, CancellationToken cancellationToken = default)
    {
        await connections.DeleteAsync(new SourceConnectionId(sourceId), cancellationToken);
        return NoContent();
    }
}
