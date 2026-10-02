using Comuki.Modules.Procedures.Domain.Definitions;
using Comuki.Modules.Procedures.Domain.Layering.Results;

namespace Comuki.Modules.Procedures.Application.Patches.Helpers;

/// <summary>
/// Adapter that projects a <see cref="LayeredProcedure"/>
/// into the <see cref="ProcedureGraph"/> shape the GraphPatch applier
/// and validator consume. Layered-procedure carries the project's
/// graph; the base version's graph is the same data — but the adapter
/// keeps the seam explicit and stable if the layered-procedure shape
/// grows (e.g. resolved kind refs) without the patch flow having to
/// track those changes.
/// </summary>
internal static class LayeredProcedureGraphAdapter
{
    /// <summary>Projects the layered procedure's nodes and edges into the graph shape.</summary>
    /// <param name="layered">The layered procedure to project.</param>
    /// <returns>The graph the patch flow consumes.</returns>
    public static ProcedureGraph ToProcedureGraph(this LayeredProcedure layered)
    {
        return new ProcedureGraph(
            Nodes: [.. layered.Nodes],
            Edges: [.. layered.Edges]);
    }
}
