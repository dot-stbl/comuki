namespace Comuki.Modules.Procedures.Domain.Layering.Model;

/// <summary>
/// Selects which project procedure a repository executes against, and
/// pins the control-plane refs (profiles, verifier catalog, capability
/// catalog) the procedure resolves to at compile time. A binding is
/// selection-only — it cannot introduce control flow of its own
/// (spec requirement, design decision 2).
/// 
/// <para>
/// In practice the refs land on the bind form by the platform's
/// pipeline (see <c>control-plane/{ref,profiles,verifiers}/</c>); the
/// binding records which control-plane ref each instance uses. The
/// compile gate (task 2.3) resolves these refs against the pinned
/// catalogs. The optional
/// <see cref="AdditionalNodes"/>/<see cref="AdditionalEdges"/>
/// slots exist for task 2.2 to verify the safety check: any non-empty
/// value here triggers a layering refusal (<c>LayeredProcedure</c>
/// cannot import control flow through a binding).
/// </para>
/// </summary>
/// <param name="RepositoryId">
/// Stable identifier of the bound repository. One project procedure
/// may serve several repositories of one project (spec scenario:
/// "Repository binds to a project procedure").
/// </param>
/// <param name="ProcedureName">
/// The <see cref="Definitions.ProcedureDefinition.Name"/> this
/// repository runs against — must match a name published by the
/// project. Resolved at compile time against the project's compiled
/// procedure list.
/// </param>
/// <param name="PinnedCatalogRefs">
/// Control-plane refs the procedure should resolve to on this
/// repository. Refs are opaque identifiers — the compile gate looks
/// them up against the catalogs.
/// </param>
/// <param name="AdditionalNodes">
/// Slots reserved for per-repository graph extensions. Layering
/// refuses a binding whose slot is non-empty (a binding cannot add
/// nodes — it can only select).
/// </param>
/// <param name="AdditionalEdges">
/// Slots reserved for per-repository graph extensions. Layering
/// refuses a binding whose slot is non-empty (a binding cannot add
/// edges).
/// </param>
public sealed record RepositoryBinding(
    string RepositoryId,
    string ProcedureName,
    IReadOnlySet<string> PinnedCatalogRefs,
    IReadOnlyList<Definitions.Elements.ProcedureNode>? AdditionalNodes = null,
    IReadOnlyList<Definitions.Elements.ProcedureEdge>? AdditionalEdges = null);
