using Comuki.Modules.Procedures.Domain.Definitions;
using Comuki.Modules.Procedures.Domain.Definitions.Elements;

namespace Comuki.Modules.Procedures.Application.Materialization.Helpers;

/// <summary>
/// The materializer's computations, extracted per the
/// no-private-methods rule: topological depths (Kahn-style) and the
/// per-node dispatch projection onto the owner-surface types.
/// </summary>
internal static class MaterializationCompute
{
    /// <summary>
    /// Projects a node to its dispatch target. Today every catalog kind
    /// dispatches as a work item; the decision/broker/brain surfaces
    /// arrive with their runtime hosts and will branch here by owner
    /// surface.
    /// </summary>
    /// <param name="node">The node being projected.</param>
    /// <param name="depth">The node's topological depth.</param>
    /// <returns>The typed dispatch for the node.</returns>
    public static Dispatch.NodeDispatch CreateDispatch(ProcedureNode node, int depth)
    {
        return new Dispatch.WorkItemDispatch(node.Id, node.KindKey, depth, node.Parameters);
    }

    /// <summary>
    /// Kahn-style topological depths: roots get 0, every other node
    /// gets one past its deepest predecessor. Nodes unreachable through
    /// the edge set (orphans beyond a cycle) fall back to depth 0.
    /// </summary>
    /// <param name="graph">The compiled procedure graph.</param>
    /// <returns>Depth by node id; every node id is present.</returns>
    public static Dictionary<string, int> ComputeDepths(ProcedureGraph graph)
    {
        var incoming = graph.Edges
            .GroupBy(static edge => edge.ToNodeId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);

        var byId = graph.Nodes.ToDictionary(static node => node.Id, StringComparer.Ordinal);
        var depths = new Dictionary<string, int>(StringComparer.Ordinal);
        var queue = new Queue<string>(
            graph.Nodes.Where(node => !incoming.ContainsKey(node.Id))
                .Select(static node => node.Id));

        foreach (var nodeId in queue)
        {
            depths[nodeId] = 0;
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var currentDepth = depths[current];

            foreach (var edge in graph.Edges.Where(edge => edge.FromNodeId == current))
            {
                if (!byId.ContainsKey(edge.ToNodeId) || depths.ContainsKey(edge.ToNodeId))
                {
                    continue;
                }

                incoming[edge.ToNodeId] = incoming.GetValueOrDefault(edge.ToNodeId) - 1;
                if (incoming[edge.ToNodeId] <= 0)
                {
                    depths[edge.ToNodeId] = currentDepth + 1;
                    queue.Enqueue(edge.ToNodeId);
                }
            }
        }

        foreach (var node in graph.Nodes.Where(node => !depths.ContainsKey(node.Id)))
        {
            depths[node.Id] = 0;
        }

        return depths;
    }
}
