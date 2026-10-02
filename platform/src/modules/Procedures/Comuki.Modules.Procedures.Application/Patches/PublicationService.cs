using Comuki.Modules.Procedures.Application.Compiler;
using Comuki.Modules.Procedures.Application.Compiler.Model;
using Comuki.Modules.Procedures.Application.Patches.Events;
using Comuki.Modules.Procedures.Application.Patches.Helpers;
using Comuki.Modules.Procedures.Application.ProcedureVersions;
using Comuki.Modules.Procedures.Domain.Patches;
using Comuki.Modules.Procedures.Domain.Patches.Model;
using Comuki.Modules.Procedures.Domain.Patches.Publication;
using Comuki.Shared.Contracts;
namespace Comuki.Modules.Procedures.Application.Patches;

/// <summary>
/// Default <see cref="IPublicationService"/> implementation. The
/// publication flow is the single point of truth for the spec scenario
/// "Brain proposes patches, humans publish":
///
/// <list type="number">
///   <item>The drafter must be an <see cref="GraphPatchDraftedKind.Operator"/>
///   or <see cref="GraphPatchDraftedKind.System"/> — a brain draft
///   cannot reach the publication step (typed refusal
///   <c>procedures.publication.brain_draft_not_publishable</c>).</item>
///   <item>The human approver must be distinct from the drafter —
///   the same person cannot draft and approve (typed refusal
///   <c>procedures.publication.approver_same_as_drafter</c>).</item>
///   <item>The patch must validate against the base graph (graph-shape
///   consistency at task 3.1).</item>
///   <item>The patch must pass the forbidden-surface check (autonomy
///   floor, budget ceiling, kind allowlist).</item>
///   <item>The patched graph is compiled; the new version is persisted
///   to the version store (write-once, content-deduplicated).</item>
///   <item>The outbox carries the <c>procedures.procedure.published.v1</c>
///   event with the from/to version ids, the approver, and (when
///   applicable) the patch id.</item>
/// </list>
///
/// The publish-rights checks live in
/// <see cref="PublicationRights"/>; the graph projection in
/// <see cref="LayeredProcedureGraphAdapter"/>.
/// </summary>
/// <param name="compiler">The deterministic compile gate (task 2.3).</param>
/// <param name="versionStore">The compiled-version store (task 2.4).</param>
/// <param name="outbox">The platform's outbox seam (host-wired).</param>
/// <param name="clock">Wall clock stamping the publication event's PublishedAt.</param>
public sealed class PublicationService(
    IProcedureCompiler compiler,
    IProcedureVersionStore versionStore,
    IOutbox outbox,
    TimeProvider clock) : IPublicationService
{
    private const string ProcedurePublishedV1Event = "procedures.procedure.published.v1";

    /// <inheritdoc />
    public async Task<CompiledProcedureVersion> PublishAsync(
        PublicationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Approver))
        {
            throw new PublicationException(
                PublicationException.ApproverEmpty,
                "Approver must not be empty.");
        }

        PublicationRights.Enforce(request);

        var baseGraph = request.LayeredProcedure.ToProcedureGraph();
        GraphPatchValidator.Validate(baseGraph, request.Patch);
        PublicationSurfaceValidator.Validate(baseGraph, request.Patch, request.Context);

        var compileInput = new ProcedureDefinition(
            ProjectId: request.Patch.ProjectId,
            ProcedureKey: request.Patch.ProcedureKey,
            GitRef: $"patch:{request.Patch.Id.Value:N}",
            Graph: GraphPatchApplier.Apply(baseGraph, request.Patch));

        var newVersion = await compiler.CompileAsync(compileInput, cancellationToken);
        await versionStore.SaveAsync(newVersion, cancellationToken);

        var integrationEvent = new ProcedurePublishedV1(
            ProjectId: request.Patch.ProjectId,
            ProcedureKey: request.Patch.ProcedureKey,
            FromVersionId: request.Patch.BaseVersionId,
            ToVersionId: newVersion.VersionId,
            PatchId: request.Patch.Id,
            PublishedBy: request.Approver,
            PublishedAt: clock.GetUtcNow());

        await outbox.PublishAsync(ProcedurePublishedV1Event, integrationEvent, cancellationToken);

        return newVersion;
    }
}
