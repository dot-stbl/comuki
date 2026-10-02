using Comuki.Modules.Procedures.Domain.Definitions;
using Comuki.Modules.Procedures.Domain.Definitions.Elements;
using Comuki.Modules.Procedures.Domain.Patches.Model;

namespace Comuki.Modules.Procedures.Domain.Patches.Diff;

/// <summary>
/// Computes the semantic <see cref="GraphPatchDiff"/> of a
/// <see cref="GraphPatch"/> against the base <see cref="ProcedureGraph"/>
/// it was drafted against. Pure function: identical inputs produce an
/// identical diff. The Application layer's <c>IGraphPatchDiffService</c>
/// wraps this with the version store; tests construct it directly.
///
/// <para>
/// Buckets the diff follows the four spec verbs:
/// <list type="bullet">
///   <item><b>add</b> — every <c>AddNode</c> op contributes its node to
///   <see cref="GraphPatchDiff.AddedNodes"/>.</item>
///   <item><b>remove</b> — every <c>RemoveNode</c> op contributes its id
///   to <see cref="GraphPatchDiff.RemovedNodeIds"/>; every base edge
///   that touched the removed node (source or destination) becomes an
///   <see cref="GraphPatchDiff.OrphanedEdge"/>.</item>
///   <item><b>rewire</b> — every <c>RewireEdge</c> op where
///   <c>Before != After</c> contributes a
///   <see cref="GraphPatchDiff.RewiredEdge"/>; no-op rewires are dropped.</item>
///   <item><b>re-parameterize</b> — every <c>ReParameterizeNode</c> op
///   whose new map differs from the base contributes a
///   <see cref="GraphPatchDiff.ReParameterizedNode"/> with the per-parameter delta;
///   identical maps are dropped.</item>
/// </list>
/// </para>
/// </summary>
public static class GraphPatchDiffComputer
{
    /// <summary>
    /// Computes the diff of <paramref name="patch"/> against
    /// <paramref name="baseGraph"/>. The base graph is not mutated and
    /// the patch is not applied — the diff is read straight off the
    /// operations and the base, in the order the brain composed them.
    /// </summary>
    /// <param name="baseGraph">The graph the patch was drafted against.</param>
    /// <param name="patch">The patch whose diff to compute.</param>
    /// <returns>The semantic diff grouped by the four spec verbs.</returns>
    public static GraphPatchDiff Compute(ProcedureGraph baseGraph, GraphPatch patch)
    {
        var addedNodes = new List<ProcedureNode>();
        var removedNodeIds = new List<string>();
        var rewiredEdges = new List<GraphPatchDiff.RewiredEdge>();
        var orphanedEdges = new List<GraphPatchDiff.OrphanedEdge>();
        var reparamNodes = new List<GraphPatchDiff.ReParameterizedNode>();

        var orphanedNodeIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var operation in patch.Operations)
        {
            switch (operation)
            {
                case GraphPatchOperation.AddNode add:
                    addedNodes.Add(add.Node);
                    break;
                case GraphPatchOperation.RemoveNode remove:
                    removedNodeIds.Add(remove.NodeId);
                    orphanedNodeIds.Add(remove.NodeId);
                    break;
                case GraphPatchOperation.RewireEdge rewire:
                    if (!rewire.Before.Equals(rewire.After))
                    {
                        rewiredEdges.Add(new GraphPatchDiff.RewiredEdge(rewire.Before, rewire.After));
                    }

                    break;
                case GraphPatchOperation.ReParameterizeNode reparam:
                    var baseNode = baseGraph.Nodes.FirstOrDefault(node => string.Equals(node.Id, reparam.NodeId, StringComparison.Ordinal));
                    if (baseNode is null)
                    {
                        break;
                    }

                    if (MapsEqual(baseNode.Parameters, reparam.NewParameters))
                    {
                        break;
                    }

                    reparamNodes.Add(new GraphPatchDiff.ReParameterizedNode(
                        NodeId: reparam.NodeId,
                        Before: baseNode.Parameters,
                        After: reparam.NewParameters,
                        Changes: ComputeParameterChanges(baseNode.Parameters, reparam.NewParameters)));
                    break;
                default:
                    throw new GraphPatchException(
                        GraphPatchException.ConflictingOperations,
                        $"Unknown GraphPatchOperation type '{operation.GetType().FullName}'; the closed hierarchy should reject this at compile time.");
            }
        }

        if (orphanedNodeIds.Count > 0)
        {
            foreach (var edge in baseGraph.Edges)
            {
                if (orphanedNodeIds.Contains(edge.FromNodeId) || orphanedNodeIds.Contains(edge.ToNodeId))
                {
                    orphanedEdges.Add(new GraphPatchDiff.OrphanedEdge(edge));
                }
            }
        }

        return new GraphPatchDiff(
            addedNodes: addedNodes,
            removedNodeIds: removedNodeIds,
            rewiredEdges: rewiredEdges,
            orphanedEdges: orphanedEdges,
            reParameterizedNodes: reparamNodes);
    }

    /// <summary>Ordinal equality of two parameter maps — key set and values compared pair-wise.</summary>
    /// <param name="left">The base parameter map.</param>
    /// <param name="right">The patched parameter map.</param>
    /// <returns>True when both maps hold the same keys with equal values.</returns>
    public static bool MapsEqual(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var pair in left)
        {
            if (!right.TryGetValue(pair.Key, out var other) || !string.Equals(pair.Value, other, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Per-parameter delta between the base and patched maps: added
    /// keys (null Before), removed keys (null After), and changed
    /// values (both non-null).
    /// </summary>
    /// <param name="before">The base parameter map.</param>
    /// <param name="after">The patched parameter map.</param>
    /// <returns>The per-parameter change list, empty when the maps are equal.</returns>
    public static IReadOnlyList<GraphPatchDiff.ParameterChange> ComputeParameterChanges(
        IReadOnlyDictionary<string, string> before,
        IReadOnlyDictionary<string, string> after)
    {
        var changes = new List<GraphPatchDiff.ParameterChange>();

        foreach (var pair in after)
        {
            if (!before.TryGetValue(pair.Key, out var beforeValue))
            {
                changes.Add(new GraphPatchDiff.ParameterChange(Name: pair.Key, Before: null, After: pair.Value));
            }
            else if (!string.Equals(beforeValue, pair.Value, StringComparison.Ordinal))
            {
                changes.Add(new GraphPatchDiff.ParameterChange(Name: pair.Key, Before: beforeValue, After: pair.Value));
            }
        }

        foreach (var pair in before)
        {
            if (!after.ContainsKey(pair.Key))
            {
                changes.Add(new GraphPatchDiff.ParameterChange(Name: pair.Key, Before: pair.Value, After: null));
            }
        }

        return changes;
    }
}
