using Comuki.Host.Procedures.Helpers;
using Comuki.Host.Procedures.Models;
using Comuki.Modules.Identity.Application.Permissions;
using Comuki.Modules.Procedures.Application.Patches;
using Comuki.Modules.Procedures.Application.Patches.Chat;
using Comuki.Modules.Procedures.Application.Patches.Drafting;
using Comuki.Modules.Procedures.Application.ProcedureVersions;
using Comuki.Modules.Procedures.Application.Queries;
using Comuki.Modules.Procedures.Application.Runtime.Trace.Storage;
using Comuki.Modules.Procedures.Domain.Patches.Diff;
using Comuki.Modules.Procedures.Domain.Patches.Model;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
namespace Comuki.Host.Procedures.Controllers;

/// <summary>
/// MVC surface of the Procedures module (task 7.1 + 7.2). Read endpoints
/// demand <c>procedure:read</c>; the propose-patch + publish endpoints
/// demand <c>procedure:write</c> (brain sessions get this via the chat
/// surface, not by calling the endpoint directly). Routes are
/// constant-typed (see <see cref="ApiRoutes"/>) — no inline literals in
/// <c>[Route]</c>.
/// </summary>
/// <param name="pinResolver">Resolves the current attempt pin to a concrete version id.</param>
/// <param name="versionHandler">Reads a compiled procedure version by id.</param>
/// <param name="proposePatchHandler">Drafts a GraphPatch from a chat-side request.</param>
/// <param name="publicationService">Publishes a human-approved patch — the brain cannot reach this path.</param>
/// <param name="traceStore">Reads the planned-vs-observed trace for a run (task 5.3 / task 7.2).</param>
[ApiController]
[Route(ApiRoutes.Procedures)]
public sealed class ProceduresController(
    IAttemptPinResolver pinResolver,
    GetProcedureVersionHandler versionHandler,
    ProposePatchFromChatHandler proposePatchHandler,
    IPublicationService publicationService,
    IProcedureTraceStore traceStore) : ControllerBase
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
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
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
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
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
    /// Returns the planned-vs-observed trace for one procedure-pinned
    /// run — the timeline Studio's Replay panel renders. 404 when the
    /// run has not been admitted against a procedure (no seeded trace).
    /// </summary>
    /// <param name="runId">The run whose trace to read.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    [HttpGet("runs/{runId:guid}/trace")]
    [RequiresPermission("procedure:read")]
    [ProducesResponseType<ProcedureTraceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProcedureTraceResponse>> ReadRunTraceAsync(
        [FromRoute] Guid runId,
        CancellationToken cancellationToken = default)
    {
        var trace = await traceStore.ReadAsync(runId, cancellationToken);
        return trace is null
            ? NotFound()
            : Ok(ProcedureTraceResponse.From(runId, trace));
    }

    /// <summary>
    /// Returns the Live run projection for one procedure-pinned run —
    /// the pinned version chrome, the timeline, and (when the runtime
    /// has stamped a comparison) the drift verdict. Composed from the
    /// trace store, which already carries the pin metadata seeded at
    /// admission. The endpoint is a single read; the dashboard's Live
    /// run mode hits it instead of stitching trace + runs/{id} on the
    /// client.
    /// </summary>
    /// <param name="runId">The run whose live projection to read.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    [HttpGet("runs/{runId:guid}/live")]
    [RequiresPermission("procedure:read")]
    [ProducesResponseType<ProcedureLiveResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProcedureLiveResponse>> ReadRunLiveAsync(
        [FromRoute] Guid runId,
        CancellationToken cancellationToken = default)
    {
        var trace = await traceStore.ReadAsync(runId, cancellationToken);
        return Ok(ProcedureLiveResponse.From(runId, trace));
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
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
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

        // Real diff payload — Studio renders the diff buckets (added
        // nodes, removed node ids, rewired edges, orphaned edges,
        // re-parameterized nodes) inline; the DiffSummary line gives the
        // header strip and the patches unchanged-flag tells Studio
        // whether to surface "no-op" copy.
        return Ok(new ProposedPatchResponse(
            PatchId: result.Patch.Id.ToString(),
            BaseVersionId: result.Patch.BaseVersionId,
            Rationale: result.Patch.Rationale,
            Unchanged: result.Diff.IsUnchanged,
            DiffSummary: BuildDiffSummary(result.Diff),
            AddedNodes: [.. result.Diff.AddedNodes
                .Select(static node => new ProposedPatchResponse.AddedNodeDto(
                    node.Id, node.KindKey, node.Parameters))],
            RemovedNodeIds: [.. result.Diff.RemovedNodeIds],
            RewiredEdges: [.. result.Diff.RewiredEdges
                .Select(static rewire => new ProposedPatchResponse.RewiredEdgeDto(
                    new ProposedPatchResponse.EdgeRefDto(rewire.Before.FromNodeId, rewire.Before.FromPort, rewire.Before.ToNodeId),
                    new ProposedPatchResponse.EdgeRefDto(rewire.After.FromNodeId, rewire.After.FromPort, rewire.After.ToNodeId)))],
            ReParameterizedNodes: [.. result.Diff.ReParameterizedNodes
                .Select(static rep => new ProposedPatchResponse.ReParameterizedNodeDto(
                    rep.NodeId,
                    [.. rep.Changes
                        .Select(static change => new ProposedPatchResponse.ParameterChangeDto(
                            change.Name, change.Before, change.After))]))]));
    }

    /// <summary>
    /// Publishes a human-approved patch: validates the patch against the
    /// base graph + policy context, runs the deterministic compile gate,
    /// persists the new compiled version, and writes the
    /// <c>procedures.procedure.published.v1</c> outbox event. The brain
    /// cannot reach this endpoint; only an authenticated human with
    /// <c>procedure:write</c> can publish (the publication-rights check
    /// inside the service refuses brain drafts).
    /// </summary>
    /// <param name="projectId">The project the procedure belongs to.</param>
    /// <param name="procedureKey">Stable key identifying the procedure.</param>
    /// <param name="request">The full publication payload — patch, layered
    /// procedure, policy context, approver.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    [HttpPost("{projectId:guid}/{procedureKey}/publish")]
    [RequiresPermission("procedure:write")]
    [ProducesResponseType<PublicationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PublicationResponse>> PublishAsync(
        [FromRoute] Guid projectId,
        [FromRoute] string procedureKey,
        [FromBody] PublicationRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var publicationRequest = PublicationRequestMapper.ToDomain(request);
        var newVersion = await publicationService.PublishAsync(publicationRequest, cancellationToken);
        return Ok(PublicationResponse.From(newVersion));
    }

    /// <summary>
    /// Builds a short human-readable summary of the diff: "added N, removed
    /// M, rewired K, re-parameterized R". Studio surfaces this in the
    /// patch header; the per-bucket arrays below carry the detail.
    /// </summary>
    private static string BuildDiffSummary(GraphPatchDiff diff)
    {
        if (diff.IsUnchanged)
        {
            return "no-op patch — the graph is unchanged";
        }

        var parts = new List<string>(4);
        if (diff.AddedNodes.Count > 0)
        {
            parts.Add($"added {diff.AddedNodes.Count}");
        }
        if (diff.RemovedNodeIds.Count > 0)
        {
            parts.Add($"removed {diff.RemovedNodeIds.Count}");
        }
        if (diff.RewiredEdges.Count > 0)
        {
            parts.Add($"rewired {diff.RewiredEdges.Count}");
        }
        if (diff.ReParameterizedNodes.Count > 0)
        {
            parts.Add($"re-parameterized {diff.ReParameterizedNodes.Count}");
        }
        return string.Join(", ", parts);
    }
}
