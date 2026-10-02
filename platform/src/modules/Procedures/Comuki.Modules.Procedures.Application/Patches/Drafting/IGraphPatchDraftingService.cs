using Comuki.Modules.Procedures.Domain.Patches;
using Comuki.Modules.Procedures.Domain.Patches.Model;

namespace Comuki.Modules.Procedures.Application.Patches.Drafting;

/// <summary>
/// Application-layer seam the brain uses to draft a
/// <see cref="GraphPatch"/>. The brain calls this with the base version
/// it is reasoning against and the operations it wants to propose; the
/// service stamps the patch with a fresh id, the drafted-by identity,
/// and the timestamp, and returns the durable proposal — never the
/// compiled result. Publication (task 3.2) is a separate step gated by
/// human approval and the compile gate.
/// 
/// <para>
/// The interface is intentionally narrow: the brain cannot influence the
/// id, the timestamp, or the draft kind — those are stamped server-side
/// so a brain that mis-reports <c>DraftedBy.Kind</c> cannot widen its
/// own surface. The chat-side wiring lives in task 8.1; this interface
/// is the contract that wiring resolves against.
/// </para>
/// </summary>
public interface IGraphPatchDraftingService
{
    /// <summary>
    /// Drafts a <see cref="GraphPatch"/> for the brain. The returned
    /// patch is durable (id, timestamp, identity) but not yet validated
    /// or applied — the caller decides whether to render the diff
    /// (via <see cref="Diffing.IGraphPatchDiffService"/>) or to surface it as a
    /// proposal in Studio.
    /// </summary>
    /// <param name="request">The brain-side draft request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GraphPatch> DraftAsync(
        DraftGraphPatchRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Input to <see cref="IGraphPatchDraftingService.DraftAsync"/>. The
/// request carries only the patch's content; identity, id, and timestamp
/// are stamped server-side.
/// </summary>
/// <param name="BaseVersionId">
/// The content-addressed compiled version the patch was drafted against.
/// </param>
/// <param name="ProjectId">The project the procedure belongs to.</param>
/// <param name="ProcedureKey">
/// Stable key the project uses to reference the procedure.
/// </param>
/// <param name="Operations">The patch's typed operations, in order.</param>
/// <param name="Rationale">Free-form explanation of the patch's intent.</param>
/// <param name="DraftedBy">Who produced the patch and how.</param>
public sealed record DraftGraphPatchRequest(
    string BaseVersionId,
    Guid ProjectId,
    string ProcedureKey,
    IReadOnlyList<GraphPatchOperation> Operations,
    string Rationale,
    GraphPatchDraftedBy DraftedBy);
