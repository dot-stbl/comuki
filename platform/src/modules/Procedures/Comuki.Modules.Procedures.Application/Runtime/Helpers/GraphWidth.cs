using Comuki.Modules.Procedures.Domain.Definitions;

namespace Comuki.Modules.Procedures.Application.Runtime.Helpers;

/// <summary>
/// Width metric for the drift classifier — the widest topological
/// level of a graph. Extracted per the no-private-methods rule.
/// </summary>
internal static class GraphWidth
{
    /// <summary>
    /// Computes the graph's max width: the number of nodes at the
    /// busiest topological depth (Kahn-style depths, roots at 0,
    /// orphans fall back to 0).
    /// </summary>
    /// <param name="graph">The graph to measure.</param>
    /// <returns>The count of nodes at the widest depth; 0 for an empty graph.</returns>
    public static int MaxWidth(ProcedureGraph graph)
    {
        if (graph.Nodes.Count == 0)
        {
            return 0;
        }

        var incoming = graph.Edges
            .GroupBy(static edge => edge.ToNodeId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);

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
            foreach (var edge in graph.Edges.Where(edge => edge.FromNodeId == current))
            {
                if (depths.ContainsKey(edge.ToNodeId))
                {
                    continue;
                }

                incoming[edge.ToNodeId] = incoming.GetValueOrDefault(edge.ToNodeId) - 1;
                if (incoming[edge.ToNodeId] <= 0)
                {
                    depths[edge.ToNodeId] = depths[current] + 1;
                    queue.Enqueue(edge.ToNodeId);
                }
            }
        }

        foreach (var node in graph.Nodes.Where(node => !depths.ContainsKey(node.Id)))
        {
            depths[node.Id] = 0;
        }

        return depths.Values.Count == 0
            ? 0
            : depths.Values.GroupBy(static d => d).Max(static group => group.Count());
    }
}
