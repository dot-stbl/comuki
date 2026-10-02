using Comuki.Modules.Procedures.Domain.Ids;
using Comuki.Modules.Procedures.Domain.Patches.Model;

namespace Comuki.Modules.Procedures.Domain.Patches;

/// <summary>
/// The durable GraphPatch proposal — the brain-facing primitive the spec
/// pins (design decision 5: "GraphPatch is a durable proposal object, not
/// an edit"). A patch references the immutable compiled version it was
/// drafted against, names the typed operations it carries, explains why,
/// and is signed by the actor that produced it. Publication (task 3.2)
/// is a separate step: this record is draft-only and the compile gate
/// re-runs the validate/diff path before a human can approve.
/// 
/// <para>
/// The proposal is content-stamped but not content-addressed (no version
/// id): two drafts from the same actor with the same operations and
/// rationale are distinct patches because they have distinct
/// <see cref="Id"/>s. Audit replay and replay's planned-vs-observed
/// trace (task 5.3) refer to patches by id, so duplicate proposals are
/// kept separate on purpose.
/// </para>
/// </summary>
/// <param name="Id">Strong-typed patch id (UUID v7, time-ordered).</param>
/// <param name="BaseVersionId">
/// The content-addressed compiled version the patch was drafted against.
/// All <see cref="GraphPatchOperation"/> rewrites and removes reference
/// ids that exist in this version's graph.
/// </param>
/// <param name="ProjectId">The project the procedure belongs to.</param>
/// <param name="ProcedureKey">
/// Stable key the project uses to reference the procedure (matches
/// <c>CompiledProcedureVersion.ProcedureKey</c> for <paramref name="BaseVersionId"/>).
/// </param>
/// <param name="Operations">
/// The patch's typed operations, applied in order by the applier.
/// Ordered so the brain can express "add this node, then rewire to it".
/// </param>
/// <param name="Rationale">
/// Free-form human-readable explanation of the patch's intent. Surfaced
/// to operators in Studio alongside the diff; required for brain drafts
/// (operators expect to see why the LLM proposed this change before
/// they publish).
/// </param>
/// <param name="DraftedBy">Who produced the patch and how.</param>
/// <param name="DraftedAt">When the patch was drafted (UTC).</param>
public sealed record GraphPatch(
    GraphPatchId Id,
    string BaseVersionId,
    Guid ProjectId,
    string ProcedureKey,
    IReadOnlyList<GraphPatchOperation> Operations,
    string Rationale,
    GraphPatchDraftedBy DraftedBy,
    DateTimeOffset DraftedAt);
