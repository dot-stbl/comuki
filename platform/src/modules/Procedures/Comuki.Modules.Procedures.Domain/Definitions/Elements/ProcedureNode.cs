using Comuki.Modules.Procedures.Domain.Catalog;

namespace Comuki.Modules.Procedures.Domain.Definitions.Elements;

/// <summary>
/// One node in a procedure definition graph. The graph is what the
/// layering merge produces (task 2.2); the compile gate (task 2.3)
/// reads the same shape to validate against the catalog and emit a
/// content-addressed version. A node carries its stable id within the
/// procedure, the catalog key of the kind it instances
/// (<see cref="NodeKindCatalog"/>),
/// and a parameter set the kind reads at compile time. Node ids are
/// scoped to the procedure — they are graph-local identifiers that edges
/// reference, not stable across the platform.
/// </summary>
/// <param name="Id">
/// Graph-local identifier — distinct from the catalog <c>KindKey</c>.
/// Stable within one procedure, used by edges to point at the node.
/// Duplicate Ids inside one <see cref="ProcedureDefinition"/> are a
/// layering refusal (conflict-as-error per design decision 2).
/// </param>
/// <param name="KindKey">
/// Reference to a kind descriptor in the pinned catalog
/// (<c>verify</c>, <c>human-gate</c>, …). The compile gate refuses a
/// definition whose kind key is absent from the catalog (spec
/// scenario: "Unknown node kind is rejected").
/// </param>
/// <param name="Parameters">
/// Per-kind parameter overrides — the kind declares a default
/// parameter schema (v1: a string name), a procedure may override
/// the inputs the kind instance receives. Stored as a string map for
/// v1; structural validation arrives in task 2.3.
/// </param>
public sealed record ProcedureNode(
    string Id,
    string KindKey,
    IReadOnlyDictionary<string, string> Parameters);
