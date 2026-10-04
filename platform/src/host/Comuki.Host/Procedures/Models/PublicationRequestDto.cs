using System.Text.Json;
using Comuki.Modules.Procedures.Application.Compiler.Model;

namespace Comuki.Host.Procedures.Models;

/// <summary>
/// Request DTO for the human-publish endpoint
/// (<c>POST /api/v1/procedures/{projectId}/{procedureKey}/publish</c>).
/// Carries the full publication payload — patch, layered procedure,
/// policy context, approver — because the wire shape is the only
/// stable contract between Studio and the host's publication service.
/// 
/// <para>
/// The brain cannot reach this endpoint; the brain drafts patches
/// (POST <c>/propose-patch</c>) and the human publishes them. The
/// in-process crown e2e (PHASE 3) calls <c>IPublicationService.PublishAsync</c>
/// directly via DI; this wire endpoint exists for Studio and any future
/// Studio-driven publishing UI.
/// </para>
/// 
/// <para>
/// The patch + layered procedure arrive as <see cref="JsonElement"/>s
/// because the domain types are not directly wire-serializable
/// (records with private setters, nested record types the kubb
/// generator cannot introspect). The host deserializes each element
/// via <see cref="JsonSerializer"/> on its way to the publication
/// service.
/// </para>
/// </summary>
/// <param name="Patch">The GraphPatch as authored by the brain (or operator).</param>
/// <param name="Approver">The human identity approving publication — must be distinct from the drafter.</param>
/// <param name="Context">Policy context — kind allowlist, autonomy ceiling, budget maxima — the publication-surface validator reads.</param>
/// <param name="LayeredProcedure">Layered merge result the publication service compiles from.</param>
public sealed record PublicationRequestDto(
    JsonElement Patch,
    string Approver,
    JsonElement Context,
    JsonElement LayeredProcedure);

/// <summary>Response DTO for the publish endpoint — the new compiled
/// version the publication produced, surfaced as the canonical
/// <c>ProcedureVersionResponse</c> so Studio can re-fetch the
/// published graph by id.</summary>
/// <param name="VersionId">The content-addressed version id of the new compiled procedure.</param>
public sealed record PublicationResponse(string VersionId)
{
    /// <summary>Maps from the Application record to the wire shape.</summary>
    public static PublicationResponse From(CompiledProcedureVersion version)
    {
        return new(version.VersionId);
    }
}
