using Comuki.Modules.Procedures.Domain.Catalog;
using Comuki.Modules.Procedures.Domain.Definitions.Elements;

namespace Comuki.Modules.Procedures.Domain.Definitions;

/// <summary>
/// The authored procedure graph — what lives in the client's git and
/// arrives at the compile gate (design decision 1). A definition is a
/// flat list of nodes plus a flat list of typed-port edges; there is no
/// nested scope — repair boundaries (task 2.3) unwrap into numbered
/// generations at materialization time. Edge ports are typed strings,
/// not free-form expressions — the compile gate matches them against the
/// source kind's declared port set.
///
/// <para>
/// The definition is the project's contribution to the layered procedure.
/// The platform's <see cref="NodeKindCatalog"/>,
/// the platform defaults, and the per-repository binding (design
/// decision 2) overlay this graph at merge time — they do not add or
/// remove nodes here. One <see cref="ProcedureDefinition"/> can back several
/// repository bindings (spec scenario: "Repository binds to a project
/// procedure").
/// </para>
/// </summary>
/// <param name="Name">
/// Human-readable project name for this procedure
/// (<c>checkout-flow</c>, <c>hotfix-flow</c>, …). Distinct from the
/// catalog's kind keys.
/// </param>
/// <param name="Nodes">
/// The graph's nodes — graph-local ids must be unique inside the
/// definition; the layering refusal surfaces the duplicate id loudly
/// when merging throws (spec scenario: "conflict throws with node
/// named").
/// </param>
/// <param name="Edges">
/// The graph's typed-port wires. Edge endpoints reference node ids
/// from <see cref="Nodes"/>; cross-graph references are not modeled
/// here — closure of the graph is a compile-gate check (task 2.3).
/// </param>
public sealed record ProcedureDefinition(
    string Name,
    IReadOnlyList<ProcedureNode> Nodes,
    IReadOnlyList<ProcedureEdge> Edges);
