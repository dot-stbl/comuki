using System.Text.Json;
using Comuki.Modules.Procedures.Application.Compiler.Model;
using Comuki.Modules.Procedures.Application.Materialization;
using Comuki.Modules.Procedures.Domain.Definitions;
using Comuki.Modules.Procedures.Domain.Definitions.Elements;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Procedures.Unit.Materialization;

/// <summary>
/// Unit tests for task 4.2: the materializer deserializes the compiled
/// graph and produces typed dispatches ordered by topological depth.
/// </summary>
public sealed class ProcedureMaterializerShould
{
    [Fact(DisplayName = "Given a linear graph, when materialized, then dispatches are ordered by depth")]
    public void LinearGraphOrdersByDepth()
    {
        var graph = new ProcedureGraph(
            Nodes: [Node("a", "intake"), Node("b", "verify"), Node("c", "complete")],
            Edges: [Edge("a", "accepted", "b"), Edge("b", "passed", "c")]);

        var plan = ProcedureMaterializer.Materialize(Version(graph));

        plan.Dispatches.Count.ShouldBe(3);
        plan.Dispatches[0].NodeId.ShouldBe("a");
        plan.Dispatches[0].Depth.ShouldBe(0);
        plan.Dispatches[1].NodeId.ShouldBe("b");
        plan.Dispatches[1].Depth.ShouldBe(1);
        plan.Dispatches[2].NodeId.ShouldBe("c");
        plan.Dispatches[2].Depth.ShouldBe(2);
    }

    [Fact(DisplayName = "Given a branching graph, when materialized, then siblings share a depth and are sorted by id")]
    public void BranchingGraphSiblingsShareDepth()
    {
        var graph = new ProcedureGraph(
            Nodes: [Node("root", "intake"), Node("a-lane", "agent"), Node("b-lane", "agent"), Node("join", "verify")],
            Edges:
            [
                Edge("root", "accepted", "a-lane"),
                Edge("root", "accepted", "b-lane"),
                Edge("a-lane", "done", "join"),
                Edge("b-lane", "done", "join"),
            ]);

        var plan = ProcedureMaterializer.Materialize(Version(graph));

        plan.Dispatches.Count.ShouldBe(4);
        plan.Dispatches[0].NodeId.ShouldBe("root");
        plan.Dispatches[0].Depth.ShouldBe(0);
        plan.Dispatches[1].NodeId.ShouldBe("a-lane");
        plan.Dispatches[1].Depth.ShouldBe(1);
        plan.Dispatches[2].NodeId.ShouldBe("b-lane");
        plan.Dispatches[2].Depth.ShouldBe(1);
        plan.Dispatches[3].NodeId.ShouldBe("join");
        plan.Dispatches[3].Depth.ShouldBe(2);
    }

    [Fact(DisplayName = "Given a single node, when materialized, then it is the sole dispatch at depth 0")]
    public void SingleNodeIsSoleDispatch()
    {
        var graph = new ProcedureGraph(Nodes: [Node("only", "intake")], Edges: []);

        var plan = ProcedureMaterializer.Materialize(Version(graph));

        plan.VersionId.ShouldBe("test-version");
        plan.Dispatches.ShouldHaveSingleItem();
        plan.Dispatches[0].NodeId.ShouldBe("only");
        plan.Dispatches[0].Depth.ShouldBe(0);
        plan.Dispatches[0].KindKey.ShouldBe("intake");
    }

    [Fact(DisplayName = "Given an empty graph, when materialized, then the plan has no dispatches")]
    public void EmptyGraphHasNoDispatches()
    {
        var graph = new ProcedureGraph(Nodes: [], Edges: []);

        var plan = ProcedureMaterializer.Materialize(Version(graph));

        plan.Dispatches.ShouldBeEmpty();
    }

    private static ProcedureNode Node(string id, string kindKey)
    {
        return new ProcedureNode(id, kindKey, new Dictionary<string, string>());
    }

    private static ProcedureEdge Edge(string from, string port, string to)
    {
        return new ProcedureEdge(from, port, to);
    }

    private static CompiledProcedureVersion Version(ProcedureGraph graph)
    {
        return new CompiledProcedureVersion(
            VersionId: "test-version",
            ProjectId: Guid.NewGuid(),
            ProcedureKey: "test-procedure",
            CatalogVersion: "1.0",
            SourceRef: "test",
            GraphJson: JsonSerializer.Serialize(graph, JsonSerializerOptions.Web));
    }
}
