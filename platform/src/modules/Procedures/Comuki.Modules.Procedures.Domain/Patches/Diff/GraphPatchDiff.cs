using Comuki.Modules.Procedures.Domain.Definitions.Elements;

namespace Comuki.Modules.Procedures.Domain.Patches.Diff;

/// <summary>
/// Semantic diff of a <see cref="GraphPatch"/> against its base version.
/// Five buckets cover the four verbs of the spec (add, remove, rewire,
/// re-parameterize) plus the cascade-orphaned edges that come with
/// <c>RemoveNode</c>. Pure data — no I/O, no DI; the diff computer
/// produces one from a base graph and a patch without touching the
/// version store.
///
/// <para>
/// <see cref="IsUnchanged"/> is true when every bucket is empty — the
/// patch is a no-op (a rewire with <c>Before == After</c>, or a
/// re-parameterize with identical maps). The validator catches the
/// obvious no-op cases (rewire equality); <see cref="IsUnchanged"/>
/// is the post-application view that catches the rest.
/// </para>
/// </summary>
public sealed record GraphPatchDiff
{
    /// <summary>
    /// One rewired edge in a <see cref="GraphPatchDiff"/> — the edge that
    /// the patch removes (<see cref="Before"/>) and the edge that replaces
    /// it (<see cref="After"/>). Same wire shape as
    /// <see cref="ProcedureEdge"/>; the diff records both so Studio can
    /// render "from → to" without re-running the applier.
    /// </summary>
    /// <param name="Before">The edge removed from the graph by the patch.</param>
    /// <param name="After">The edge inserted into the graph by the patch.</param>
    public sealed record RewiredEdge(ProcedureEdge Before, ProcedureEdge After);

    /// <summary>
    /// One edge removed from the graph because its source or destination
    /// node was deleted by a <c>RemoveNode</c> op (cascading orphan). The
    /// edge is recorded so Studio can render the cleanup; the applier
    /// does not need to surface these separately — they are removed by the
    /// node deletion.
    /// </summary>
    /// <param name="Edge">The edge removed by the cascade.</param>
    public sealed record OrphanedEdge(ProcedureEdge Edge);

    /// <summary>
    /// One per-parameter change inside a <see cref="ReParameterizedNode"/>
    /// entry. <see cref="Before"/> is null when the parameter was added;
    /// <see cref="After"/> is null when the parameter was removed; both
    /// non-null when the value changed.
    /// </summary>
    /// <param name="Name">Parameter key (the kind-declared name).</param>
    /// <param name="Before">The base value, or null when added.</param>
    /// <param name="After">The new value, or null when removed.</param>
    public sealed record ParameterChange(string Name, string? Before, string? After);

    /// <summary>
    /// One re-parameterized node entry in a <see cref="GraphPatchDiff"/>:
    /// the base parameter map, the new parameter map, and the per-parameter
    /// change list Studio renders. An entry exists only when the maps
    /// actually differ — the diff computer drops no-op re-parameterize ops.
    /// </summary>
    /// <param name="NodeId">Graph-local id of the re-parameterized node.</param>
    /// <param name="Before">The base parameter map (frozen at <c>BaseVersionId</c>).</param>
    /// <param name="After">The new parameter map from the patch.</param>
    /// <param name="Changes">Per-parameter delta from <paramref name="Before"/> to <paramref name="After"/>.</param>
    public sealed record ReParameterizedNode(
        string NodeId,
        IReadOnlyDictionary<string, string> Before,
        IReadOnlyDictionary<string, string> After,
        IReadOnlyList<ParameterChange> Changes);

    /// <summary>Nodes inserted by <c>AddNode</c> ops.</summary>
    public IReadOnlyList<ProcedureNode> AddedNodes { get; init; }

    /// <summary>Node ids removed by <c>RemoveNode</c> ops.</summary>
    public IReadOnlyList<string> RemovedNodeIds { get; init; }

    /// <summary>Edge transitions from <c>RewireEdge</c> ops where <c>Before != After</c>.</summary>
    public IReadOnlyList<RewiredEdge> RewiredEdges { get; init; }

    /// <summary>Edges cascade-orphaned by <c>RemoveNode</c> ops.</summary>
    public IReadOnlyList<OrphanedEdge> OrphanedEdges { get; init; }

    /// <summary>Per-node parameter changes from <c>ReParameterizeNode</c> ops.</summary>
    public IReadOnlyList<ReParameterizedNode> ReParameterizedNodes { get; init; }

    /// <summary>
    /// Constructs a diff with all five buckets.
    /// </summary>
    /// <param name="addedNodes">Nodes inserted by <c>AddNode</c> ops.</param>
    /// <param name="removedNodeIds">Node ids removed by <c>RemoveNode</c> ops.</param>
    /// <param name="rewiredEdges">Edge transitions from <c>RewireEdge</c> ops where <c>Before != After</c>.</param>
    /// <param name="orphanedEdges">Edges cascade-orphaned by <c>RemoveNode</c> ops.</param>
    /// <param name="reParameterizedNodes">Per-node parameter changes from <c>ReParameterizeNode</c> ops.</param>
    public GraphPatchDiff(
        IReadOnlyList<ProcedureNode> addedNodes,
        IReadOnlyList<string> removedNodeIds,
        IReadOnlyList<RewiredEdge> rewiredEdges,
        IReadOnlyList<OrphanedEdge> orphanedEdges,
        IReadOnlyList<ReParameterizedNode> reParameterizedNodes)
    {
        AddedNodes = addedNodes;
        RemovedNodeIds = removedNodeIds;
        RewiredEdges = rewiredEdges;
        OrphanedEdges = orphanedEdges;
        ReParameterizedNodes = reParameterizedNodes;
    }

    /// <summary>True when every diff bucket is empty — the patch produces no observable change.</summary>
    public bool IsUnchanged =>
        AddedNodes.Count == 0
        && RemovedNodeIds.Count == 0
        && RewiredEdges.Count == 0
        && OrphanedEdges.Count == 0
        && ReParameterizedNodes.Count == 0;

    /// <summary>True when the diff is non-empty — drives Studio's "patch has changes" badge.</summary>
    public bool HasChanges => !IsUnchanged;
}
