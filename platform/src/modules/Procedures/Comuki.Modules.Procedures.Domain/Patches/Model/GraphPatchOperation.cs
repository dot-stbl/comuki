using Comuki.Modules.Procedures.Domain.Definitions.Elements;

namespace Comuki.Modules.Procedures.Domain.Patches.Model;

/// <summary>
/// The closed hierarchy of typed operations a <see cref="GraphPatch"/>
/// carries. The brain composes patches from these primitives; the
/// validator and the diff computer each see only this type. Mirrors the
/// spec wording — "add, remove, rewire, or re-parameterize" — so the four
/// variants are exactly the verbs the diff renders in Studio (task 3.1
/// scenario: "patch's diff renders add/remove/rewire/re-parameterize
/// against its base version").
/// 
/// <para>
/// Closed via a <c>private</c> constructor on the base record: only the
/// nested types declared here are valid operations. Future verbs (for
/// example, <c>AddEdge</c> as a separate primitive if a pure add becomes
/// common) belong in this same file and the diff computer learns to
/// render them — not a string-typed <c>OperationKind</c> enum that
/// drifts at compile time.
/// </para>
/// </summary>
public abstract record GraphPatchOperation
{
    private GraphPatchOperation()
    {
    }

    /// <summary>Insert a new node into the graph; its id must not collide with a base node.</summary>
    /// <param name="Node">The full <see cref="ProcedureNode"/> to insert.</param>
    public sealed record AddNode(ProcedureNode Node) : GraphPatchOperation;

    /// <summary>
    /// Delete a node from the graph; edges that touch the removed node
    /// (as source or destination) are cascade-orphaned and surface in the
    /// diff as <c>OrphanedEdges</c> (design decision 5: edges live and die
    /// with their endpoints).
    /// </summary>
    /// <param name="NodeId">Graph-local id of the node to remove.</param>
    public sealed record RemoveNode(string NodeId) : GraphPatchOperation;

    /// <summary>
    /// Replace one typed-port edge with another. The <see cref="Before"/>
    /// edge must exist in the base (or in the post-add state when the
    /// patch first adds its source/destination node); the
    /// <see cref="After"/> edge must not collide with an existing edge in
    /// the post-rewire state. A no-op rewire (<c>Before == After</c>) is
    /// allowed and produces no diff entry.
    /// </summary>
    /// <param name="Before">The edge to remove from the graph.</param>
    /// <param name="After">The edge to insert into the graph.</param>
    public sealed record RewireEdge(ProcedureEdge Before, ProcedureEdge After) : GraphPatchOperation;

    /// <summary>
    /// Replace a node's parameter map (the kind's typed inputs) while
    /// keeping the node's id and <c>KindKey</c>. The diff records each
    /// per-parameter change so Studio can show "added", "removed", and
    /// "changed" rows separately.
    /// </summary>
    /// <param name="NodeId">Graph-local id of the node to re-parameterize.</param>
    /// <param name="NewParameters">The new parameter map; replaces the existing map wholesale.</param>
    public sealed record ReParameterizeNode(
        string NodeId,
        IReadOnlyDictionary<string, string> NewParameters) : GraphPatchOperation;
}
