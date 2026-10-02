using Comuki.Modules.Procedures.Domain.Definitions;
using Comuki.Modules.Procedures.Domain.Definitions.Elements;
using Comuki.Modules.Procedures.Domain.Patches.Diff;
using Comuki.Modules.Procedures.Domain.Patches.Model;

namespace Comuki.Modules.Procedures.Domain.Patches;

/// <summary>
/// Applies a <see cref="GraphPatch"/> to a base <see cref="ProcedureGraph"/>
/// and returns the resulting graph. Pure transformation: no I/O, no DI,
/// no exception type for "expected miss" cases — the validator at task
/// 3.1 has already refused the patch if the application would fail in a
/// way the caller can act on. The applier only throws for invariant
/// breaks the validator missed (i.e. contract bugs).
/// 
/// <para>
/// Application order is the order of <see cref="GraphPatch.Operations"/>:
/// the brain composes a patch so each step's preconditions are met by
/// the previous steps. A typical "add a node, wire it in" patch is
/// <c>[AddNode(newNode), RewireEdge(oldEdge, newEdge)]</c>; reversing
/// those ops would make the second op's <c>Before</c> refer to a stale
/// graph and fail validation. The validator enforces a forward-only
/// application shape and refuses the reverse at draft time.
/// </para>
/// </summary>
public static class GraphPatchApplier
{
    /// <summary>
    /// Applies <paramref name="patch"/> to <paramref name="baseGraph"/>
    /// and returns the resulting graph. The base graph is not mutated.
    /// </summary>
    /// <param name="baseGraph">The graph the patch was drafted against.</param>
    /// <param name="patch">The patch to apply.</param>
    /// <returns>The graph after every operation in <paramref name="patch"/> has been applied in order.</returns>
    public static ProcedureGraph Apply(ProcedureGraph baseGraph, GraphPatch patch)
    {
        var nodes = new Dictionary<string, ProcedureNode>(baseGraph.Nodes.Count, StringComparer.Ordinal);
        foreach (var node in baseGraph.Nodes)
        {
            nodes[node.Id] = node;
        }

        var edges = new List<ProcedureEdge>(baseGraph.Edges);

        foreach (var operation in patch.Operations)
        {
            switch (operation)
            {
                case GraphPatchOperation.AddNode add:
                    nodes[add.Node.Id] = add.Node;
                    break;
                case GraphPatchOperation.RemoveNode remove:
                    nodes.Remove(remove.NodeId);
                    edges.RemoveAll(edge =>
                        string.Equals(edge.FromNodeId, remove.NodeId, StringComparison.Ordinal)
                        || string.Equals(edge.ToNodeId, remove.NodeId, StringComparison.Ordinal));
                    break;
                case GraphPatchOperation.RewireEdge rewire:
                    if (rewire.Before.Equals(rewire.After))
                    {
                        break;
                    }

                    edges.RemoveAll(edge => edge.Equals(rewire.Before));
                    edges.Add(rewire.After);
                    break;
                case GraphPatchOperation.ReParameterizeNode reparam:
                    if (nodes.TryGetValue(reparam.NodeId, out var existing))
                    {
                        nodes[reparam.NodeId] = existing with
                        {
                            Parameters = reparam.NewParameters,
                        };
                    }

                    break;
                default:
                    throw new GraphPatchException(
                        GraphPatchException.ConflictingOperations,
                        $"Unknown GraphPatchOperation type '{operation.GetType().FullName}'; the closed hierarchy should reject this at compile time.");
            }
        }

        return new ProcedureGraph(
            Nodes: [.. nodes.Values],
            Edges: edges);
    }
}
