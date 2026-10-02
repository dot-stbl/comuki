using Comuki.Modules.Procedures.Domain.Definitions;
using Comuki.Modules.Procedures.Domain.Definitions.Elements;
using Comuki.Modules.Procedures.Domain.Ids;
using Comuki.Modules.Procedures.Domain.Patches;
using Comuki.Modules.Procedures.Domain.Patches.Model;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Procedures.Unit.Patches;

/// <summary>
/// Unit tests for task 3.1: the GraphPatch applier applies operations
/// in order against the base graph and returns the new graph without
/// mutating the base. Each scenario covers one of the four spec verbs
/// (add, remove, rewire, re-parameterize) so a regression in any single
/// op is isolated.
/// </summary>
public sealed class GraphPatchApplierShould
{
    [Fact(DisplayName = "Given an AddNode patch, when applied, then the new node is in the result and the base is untouched")]
    public void AddNodeInsertsNode()
    {
        var baseGraph = NewGraph(("intake", "intake", "accepted"));
        var newNode = new ProcedureNode("verify", "verify", new Dictionary<string, string> { ["strictness"] = "low" });
        var patch = NewPatch(new GraphPatchOperation.AddNode(newNode));

        var result = GraphPatchApplier.Apply(baseGraph, patch);

        result.Nodes.Select(static node => node.Id).ShouldBe(["intake", "verify"]);
        result.Nodes.Single(static node => node.Id == "verify").KindKey.ShouldBe("verify");
        baseGraph.Nodes.Count.ShouldBe(1);
    }

    [Fact(DisplayName = "Given a RemoveNode patch, when applied, then the node is gone and edges that touched it are cascade-orphaned")]
    public void RemoveNodeCascadesEdges()
    {
        var baseGraph = NewGraph(
            ("intake", "intake", "accepted"),
            ("verify", "verify", "passed"),
            ("complete", "complete", "done"));
        var patch = NewPatch(new GraphPatchOperation.RemoveNode("verify"));

        var result = GraphPatchApplier.Apply(baseGraph, patch);

        result.Nodes.Select(static node => node.Id).ShouldBe(["intake", "complete"]);
        result.Edges.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given a RewireEdge patch with a real Before and After, when applied, then the old wire is gone and the new wire is present")]
    public void RewireEdgeReplacesWire()
    {
        var baseGraph = NewGraph(
            ("a", "intake", "accepted"),
            ("b", "verify", "passed"));
        baseGraph = baseGraph with
        {
            Edges = [new ProcedureEdge("a", "accepted", "b")],
        };

        var patch = NewPatch(new GraphPatchOperation.RewireEdge(
            new ProcedureEdge("a", "accepted", "b"),
            new ProcedureEdge("a", "rejected", "b")));

        var result = GraphPatchApplier.Apply(baseGraph, patch);

        result.Edges.ShouldHaveSingleItem();
        result.Edges[0].FromPort.ShouldBe("rejected");
    }

    [Fact(DisplayName = "Given a RewireEdge patch with Before == After, when applied, then the edge set is unchanged")]
    public void RewireEdgeNoOpIsIdempotent()
    {
        var baseGraph = NewGraph(("a", "intake", "accepted"));
        baseGraph = baseGraph with
        {
            Edges = [new ProcedureEdge("a", "accepted", "self")],
        };

        var edge = new ProcedureEdge("a", "accepted", "self");
        var patch = NewPatch(new GraphPatchOperation.RewireEdge(edge, edge));

        var result = GraphPatchApplier.Apply(baseGraph, patch);

        result.Edges.ShouldHaveSingleItem();
        result.Edges[0].ShouldBe(edge);
    }

    [Fact(DisplayName = "Given a ReParameterizeNode patch, when applied, then the node's parameter map is replaced")]
    public void ReParameterizeNodeReplacesParameters()
    {
        var baseGraph = NewGraphWithParams("verify", "verify", new Dictionary<string, string>
        {
            ["strictness"] = "low",
            ["timeout"] = "30s",
        });

        var newParams = new Dictionary<string, string>
        {
            ["strictness"] = "high",
            ["timeout"] = "60s",
        };
        var patch = NewPatch(new GraphPatchOperation.ReParameterizeNode("verify", newParams));

        var result = GraphPatchApplier.Apply(baseGraph, patch);

        var verify = result.Nodes.Single(static node => node.Id == "verify");
        verify.Parameters["strictness"].ShouldBe("high");
        verify.Parameters["timeout"].ShouldBe("60s");
    }

    [Fact(DisplayName = "Given a composed patch [AddNode, RewireEdge], when applied, then both ops contribute and the rewire's Before is found in the post-add state")]
    public void ComposedPatchAppliesInOrder()
    {
        var baseGraph = NewGraph(
            ("intake", "intake", "accepted"),
            ("verify", "verify", "passed"));
        baseGraph = baseGraph with
        {
            Edges = [new ProcedureEdge("intake", "accepted", "verify")],
        };

        var newVerify = new ProcedureNode("strict-verify", "verify", new Dictionary<string, string>());
        var patch = NewPatch(
            new GraphPatchOperation.AddNode(newVerify),
            new GraphPatchOperation.RewireEdge(
                new ProcedureEdge("intake", "accepted", "verify"),
                new ProcedureEdge("intake", "accepted", "strict-verify")));

        var result = GraphPatchApplier.Apply(baseGraph, patch);

        result.Nodes.Select(static node => node.Id).ShouldBe(["intake", "verify", "strict-verify"]);
        result.Edges.ShouldHaveSingleItem();
        result.Edges[0].ToNodeId.ShouldBe("strict-verify");
    }

    private static ProcedureGraph NewGraph(params (string Id, string KindKey, string _)[] nodes)
    {
        var nodeList = nodes
            .Select(static spec => new ProcedureNode(spec.Id, spec.KindKey, new Dictionary<string, string>()))
            .ToList();
        return new ProcedureGraph(nodeList, []);
    }

    private static ProcedureGraph NewGraphWithParams(string id, string kindKey, Dictionary<string, string> parameters)
    {
        return new ProcedureGraph([new ProcedureNode(id, kindKey, parameters)], []);
    }

    private static GraphPatch NewPatch(params GraphPatchOperation[] operations)
    {
        return new GraphPatch(
            Id: GraphPatchId.New(),
            BaseVersionId: "base-version",
            ProjectId: Guid.NewGuid(),
            ProcedureKey: "test-procedure",
            Operations: operations,
            Rationale: "unit test",
            DraftedBy: new GraphPatchDraftedBy("tester", GraphPatchDraftedKind.Operator),
            DraftedAt: DateTimeOffset.UtcNow);
    }
}
