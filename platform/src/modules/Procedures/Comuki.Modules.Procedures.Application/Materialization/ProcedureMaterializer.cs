using System.Text.Json;
using Comuki.Modules.Procedures.Application.Compiler.Model;
using Comuki.Modules.Procedures.Application.Materialization.Helpers;
using Comuki.Modules.Procedures.Domain.Definitions;

namespace Comuki.Modules.Procedures.Application.Materialization;

/// <summary>
/// Deserializes the compiled graph from a pinned version and produces a
/// <see cref="MaterializationPlan"/> with typed dispatches ordered by
/// topological depth. Pure — no I/O, no DI, no model calls. The host
/// wires the dispatches to the actual work item / decision / broker
/// operation systems; the depth and projection computations live in
/// <see cref="MaterializationCompute"/>.
/// </summary>
public static class ProcedureMaterializer
{
    /// <summary>
    /// Materializes the compiled graph: deserializes GraphJson, computes
    /// topological depths, and creates typed dispatches per node based on
    /// the kind's owner surface. Nodes at the same depth are siblings
    /// (parallel); the host fans them out.
    /// </summary>
    /// <param name="version">The pinned compiled version carrying the graph.</param>
    /// <returns>The ordered materialization plan.</returns>
    public static MaterializationPlan Materialize(CompiledProcedureVersion version)
    {
        var graph = JsonSerializer.Deserialize<ProcedureGraph>(version.GraphJson, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException(
                $"Compiled version '{version.VersionId}' has a malformed GraphJson.");

        var depths = MaterializationCompute.ComputeDepths(graph);
        var dispatches = graph.Nodes
            .Select(node => new { Node = node, Depth = depths.GetValueOrDefault(node.Id, 0) })
            .OrderBy(static entry => entry.Depth)
            .ThenBy(static entry => entry.Node.Id, StringComparer.Ordinal)
            .Select(static entry => MaterializationCompute.CreateDispatch(entry.Node, entry.Depth))
            .ToList();

        return new MaterializationPlan(version.VersionId, dispatches);
    }
}
