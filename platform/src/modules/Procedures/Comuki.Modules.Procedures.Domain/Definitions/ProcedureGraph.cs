using Comuki.Modules.Procedures.Domain.Definitions.Elements;

namespace Comuki.Modules.Procedures.Domain.Definitions;

/// <summary>
/// The graph structure a compile gate validates: nodes referencing
/// catalog kind keys and edges connecting typed outcome ports. This is
/// the pure structural shape — project identity and git provenance live
/// on the Application-level wrapper.
/// </summary>
/// <param name="Nodes">The procedure's nodes, each referencing a kind key from the pinned catalog.</param>
/// <param name="Edges">Typed-port edges connecting nodes.</param>
public sealed record ProcedureGraph(
    IReadOnlyList<ProcedureNode> Nodes,
    IReadOnlyList<ProcedureEdge> Edges);
