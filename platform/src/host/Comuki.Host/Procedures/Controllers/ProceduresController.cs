using Comuki.Host.Procedures.Models;
using Comuki.Modules.Identity.Application.Permissions;
using Comuki.Modules.Procedures.Application.Patches.Chat;
using Comuki.Modules.Procedures.Application.Patches.Drafting;
using Comuki.Modules.Procedures.Application.ProcedureVersions;
using Comuki.Modules.Procedures.Application.Queries;
using Comuki.Modules.Procedures.Domain.Patches.Model;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
namespace Comuki.Host.Procedures.Controllers;

/// <summary>
/// MVC surface of the Procedures module (task 7.1). Read endpoints demand
/// <c>procedure:read</c>; the propose-patch endpoint demands
/// <c>procedure:write</c> (brain sessions get this via the chat surface,
/// not by calling the endpoint directly). Routes are constant-typed (see
/// <see cref="ApiRoutes"/>) — no inline literals in <c>[Route]</c>.
/// </summary>
/// <param name="pinResolver">Resolves the current attempt pin to a concrete version id.</param>
/// <param name="versionHandler">Reads a compiled procedure version by id.</param>
/// <param name="proposePatchHandler">Drafts a GraphPatch from a chat-side request.</param>
[ApiController]
[Route(ApiRoutes.Procedures)]
public sealed class ProceduresController(
    IAttemptPinResolver pinResolver,
    GetProcedureVersionHandler versionHandler,
    ProposePatchFromChatHandler proposePatchHandler) : ControllerBase
{
    /// <summary>
    /// Returns the current compiled version of a procedure — the one the
    /// attempt pin points at. 404 when no pin has been published yet.
    /// </summary>
    /// <param name="projectId">The project that owns the procedure.</param>
    /// <param name="procedureKey">Stable key identifying the procedure.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    [HttpGet("{projectId:guid}/{procedureKey}")]
    [RequiresPermission("procedure:read")]
    [ProducesResponseType<ProcedureVersionResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProcedureVersionResponse>> GetLatestAsync(
        [FromRoute] Guid projectId,
        [FromRoute] string procedureKey,
        CancellationToken cancellationToken = default)
    {
        var versionId = await pinResolver.ResolveCurrentVersionIdAsync(projectId, procedureKey, cancellationToken);
        if (versionId is null)
        {
            return NotFound();
        }

        var version = await versionHandler.HandleAsync(versionId, cancellationToken);
        return version is null
            ? NotFound()
            : Ok(ProcedureVersionResponse.From(version));
    }

    /// <summary>Reads one compiled version by its content-addressed id.</summary>
    /// <param name="versionId">The content-addressed version id.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    [HttpGet("versions/{versionId}")]
    [RequiresPermission("procedure:read")]
    [ProducesResponseType<ProcedureVersionResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProcedureVersionResponse>> GetVersionAsync(
        [FromRoute] string versionId,
        CancellationToken cancellationToken = default)
    {
        var version = await versionHandler.HandleAsync(versionId, cancellationToken);
        return version is null
            ? NotFound()
            : Ok(ProcedureVersionResponse.From(version));
    }

    /// <summary>
    /// Drafts a GraphPatch from a chat-side request. The patch is durable
    /// (id, timestamp, identity) but the chat surface has no publication
    /// path — the human publishes from Studio after reviewing the
    /// rendered diff.
    /// </summary>
    /// <param name="projectId">The project the procedure belongs to.</param>
    /// <param name="procedureKey">Stable key identifying the procedure.</param>
    /// <param name="request">The operator's intent (what to change, why).</param>
    /// <param name="validator"></param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    [HttpPost("{projectId:guid}/{procedureKey}/propose-patch")]
    [RequiresPermission("procedure:write")]
    [ProducesResponseType<ProposedPatchResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProposedPatchResponse>> ProposePatchAsync(
        [FromRoute] Guid projectId,
        [FromRoute] string procedureKey,
        [FromBody] ProposePatchRequest request,
        [FromServices] IValidator<ProposePatchRequest> validator,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);

        var draftRequest = new DraftGraphPatchRequest(
            BaseVersionId: request.BaseVersionId,
            ProjectId: projectId,
            ProcedureKey: procedureKey,
            Operations: [],
            Rationale: request.Rationale,
            DraftedBy: new GraphPatchDraftedBy(
                request.DraftedBy,
                GraphPatchDraftedKind.Brain));

        var result = await proposePatchHandler.HandleAsync(draftRequest, cancellationToken);

        return Ok(new ProposedPatchResponse(
            PatchId: result.Patch.Id.ToString(),
            BaseVersionId: result.Patch.BaseVersionId,
            Rationale: result.Patch.Rationale,
            DiffSummary: "See Studio for the rendered diff."));
    }
}
