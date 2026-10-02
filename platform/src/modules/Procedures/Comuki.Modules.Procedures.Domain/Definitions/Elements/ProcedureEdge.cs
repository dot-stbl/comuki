namespace Comuki.Modules.Procedures.Domain.Definitions.Elements;

/// <summary>
/// One typed-outcome-port wire between two nodes. Edges are the
/// condition graph of the procedure — there are no free-form
/// predicates, only typed ports (design decision 6: "port outcomes
/// are the only conditions"). The compile gate (task 2.3) checks that
/// every edge's <c>FromPort</c> matches a port the source kind
/// actually declares; this file only captures the shape.
/// </summary>
/// <param name="FromNodeId">
/// The source <see cref="ProcedureNode.Id"/> of the wire. Stable
/// within the procedure.
/// </param>
/// <param name="FromPort">
/// The outcome port on the source node the wire leaves through
/// (<c>passed</c>, <c>failed</c>, <c>gate.approved</c>, …). The
/// compile gate refuses an edge whose port is not declared on the
/// source kind.
/// </param>
/// <param name="ToNodeId">
/// The destination <see cref="ProcedureNode.Id"/> of the wire.
/// </param>
public sealed record ProcedureEdge(
    string FromNodeId,
    string FromPort,
    string ToNodeId);
