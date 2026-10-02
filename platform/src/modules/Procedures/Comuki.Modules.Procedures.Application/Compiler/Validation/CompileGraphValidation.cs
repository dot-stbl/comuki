using Comuki.Modules.Procedures.Application.Compiler.Model;
using Comuki.Modules.Procedures.Domain.Catalog;
using Comuki.Modules.Procedures.Domain.Definitions;
using Comuki.Modules.Procedures.Domain.Definitions.Elements;
using Comuki.Modules.Procedures.Domain.Kinds;

namespace Comuki.Modules.Procedures.Application.Compiler.Validation;

/// <summary>
/// The compile gate's structural checks, extracted per the
/// no-private-methods rule — kind resolution, port resolution,
/// acyclicity outside repair boundaries, and the fan-out ceiling.
/// Each check throws <see cref="ProcedureCompilerException"/> naming
/// the offending element; the compiler orchestrates the sequence.
/// </summary>
internal static class CompileGraphValidation
{
    /// <summary>
    /// Runs every structural check in gate order: kinds, ports,
    /// acyclicity, fan-out.
    /// </summary>
    /// <param name="graph">The procedure graph to validate.</param>
    /// <param name="kindByKey">Kind lookup from the pinned catalog.</param>
    public static void ValidateGraph(
        ProcedureGraph graph,
        IReadOnlyDictionary<string, NodeKindDescriptor> kindByKey)
    {
        ValidateNodeKinds(graph.Nodes, kindByKey);
        ValidatePorts(graph, kindByKey);
        ValidateAcyclic(graph);
        ValidateFanOut(graph, ProcedureCompiler.FanOutCeiling);
    }

    /// <summary>
    /// Builds the ordinal-compared kind lookup from the pinned catalog
    /// entries.
    /// </summary>
    /// <param name="catalog">The pinned node-kind catalog.</param>
    /// <returns>Descriptor by kind key.</returns>
    public static Dictionary<string, NodeKindDescriptor> BuildKindLookup(NodeKindCatalog catalog)
    {
        return catalog.Entries.ToDictionary(
            static entry => entry.Key,
            static entry => entry.Descriptor,
            StringComparer.Ordinal);
    }

    /// <summary>Refuses a node whose kind key is absent from the pinned catalog.</summary>
    /// <param name="nodes">The procedure graph's nodes.</param>
    /// <param name="kindByKey">Kind lookup from the pinned catalog.</param>
    public static void ValidateNodeKinds(
        IReadOnlyList<ProcedureNode> nodes,
        IReadOnlyDictionary<string, NodeKindDescriptor> kindByKey)
    {
        foreach (var node in nodes)
        {
            if (!kindByKey.ContainsKey(node.KindKey))
            {
                throw new ProcedureCompilerException(
                    ProcedureCompilerException.PortNotFound,
                    $"Node '{node.Id}' references unknown kind '{node.KindKey}'.");
            }
        }
    }

    /// <summary>Refuses an edge whose source node or outcome port does not resolve.</summary>
    /// <param name="graph">The procedure graph.</param>
    /// <param name="kindByKey">Kind lookup from the pinned catalog.</param>
    public static void ValidatePorts(
        ProcedureGraph graph,
        IReadOnlyDictionary<string, NodeKindDescriptor> kindByKey)
    {
        var nodeById = graph.Nodes.ToDictionary(
            static node => node.Id, StringComparer.Ordinal);

        foreach (var edge in graph.Edges)
        {
            if (!nodeById.TryGetValue(edge.FromNodeId, out var sourceNode))
            {
                throw new ProcedureCompilerException(
                    ProcedureCompilerException.PortNotFound,
                    $"Edge references unknown source node '{edge.FromNodeId}'.");
            }

            var descriptor = kindByKey[sourceNode.KindKey];
            var portExists = descriptor.OutcomePorts.Any(
                port => string.Equals(port.Name, edge.FromPort, StringComparison.Ordinal));
            if (!portExists)
            {
                throw new ProcedureCompilerException(
                    ProcedureCompilerException.PortNotFound,
                    $"Node '{edge.FromNodeId}' (kind '{sourceNode.KindKey}') has no port '{edge.FromPort}'; the kind declares: {string.Join(", ", descriptor.OutcomePorts.Select(static port => port.Name))}.");
            }
        }
    }

    /// <summary>
    /// Refuses a cycle anywhere in the graph — repair boundaries are the
    /// only legal cycles and are unwrapped before the compile gate runs.
    /// </summary>
    /// <param name="graph">The procedure graph.</param>
    public static void ValidateAcyclic(ProcedureGraph graph)
    {
        var adjacency = BuildAdjacency(graph);
        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        var path = new List<string>();

        foreach (var node in graph.Nodes)
        {
            if (state.GetValueOrDefault(node.Id) == 0)
            {
                DfsVisit(node.Id, adjacency, state, path);
            }
        }
    }

    /// <summary>
    /// One depth-first visit marking node state (0 unvisited, 1 in-path,
    /// 2 done); an in-path neighbour is a cycle and names the path.
    /// </summary>
    /// <param name="nodeId">The node being visited.</param>
    /// <param name="adjacency">Outgoing edges by source node id.</param>
    /// <param name="state"></param>
    /// <param name="path">The current DFS path, for the cycle message.</param>
    public static void DfsVisit(
        string nodeId,
        IReadOnlyDictionary<string, IReadOnlyList<string>> adjacency,
        Dictionary<string, int> state,
        List<string> path)
    {
        state[nodeId] = 1;
        path.Add(nodeId);

        if (adjacency.TryGetValue(nodeId, out var neighbors))
        {
            foreach (var neighbor in neighbors)
            {
                if (state.GetValueOrDefault(neighbor) == 1)
                {
                    var cycleStart = path.IndexOf(neighbor);
                    var cyclePath = string.Join(" → ", path[cycleStart..]) + " → " + neighbor;
                    throw new ProcedureCompilerException(
                        ProcedureCompilerException.CycleDetected,
                        $"Cycle detected outside repair boundaries: {cyclePath}.");
                }

                if (state.GetValueOrDefault(neighbor) == 0)
                {
                    DfsVisit(neighbor, adjacency, state, path);
                }
            }
        }

        path.RemoveAt(path.Count - 1);
    }

    /// <summary>
    /// Adjacency list from the graph's edges — the DFS input for the
    /// acyclicity check.
    /// </summary>
    /// <param name="graph">The procedure graph.</param>
    /// <returns>Destination node ids by source node id.</returns>
    public static Dictionary<string, IReadOnlyList<string>> BuildAdjacency(ProcedureGraph graph)
    {
        var adjacency = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var edge in graph.Edges)
        {
            if (!adjacency.TryGetValue(edge.FromNodeId, out var targets))
            {
                targets = [];
                adjacency[edge.FromNodeId] = targets;
            }

            targets.Add(edge.ToNodeId);
        }

        return adjacency.ToDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlyList<string>)pair.Value,
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Refuses a depth-0 fan-out above the ceiling — the roots count is
    /// the widest single level the runtime materializer may reach.
    /// </summary>
    /// <param name="graph">The procedure graph.</param>
    /// <param name="ceiling">Maximum nodes at any single depth level.</param>
    public static void ValidateFanOut(ProcedureGraph graph, int ceiling)
    {
        var incomingCount = graph.Edges
            .GroupBy(static edge => edge.ToNodeId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);

        var roots = graph.Nodes
            .Where(node => !incomingCount.ContainsKey(node.Id))
            .Select(static node => node.Id)
            .ToList();

        if (roots.Count > ceiling)
        {
            throw new ProcedureCompilerException(
                ProcedureCompilerException.FanOutExceeded,
                $"Fan-out at depth 0 is {roots.Count}, exceeding the ceiling of {ceiling}.");
        }
    }
}
