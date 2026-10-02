using Comuki.Modules.Procedures.Domain.Definitions;
using Comuki.Modules.Procedures.Domain.Definitions.Elements;
using Comuki.Modules.Procedures.Domain.Ids;
using Comuki.Modules.Procedures.Domain.Patches;
using Comuki.Modules.Procedures.Domain.Patches.Diff;
using Comuki.Modules.Procedures.Domain.Patches.Model;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Procedures.Unit.Patches;

/// <summary>
/// Unit tests for task 3.1: the GraphPatch diff computer renders
/// add / remove / rewire / re-parameterize against the base version
/// the patch was drafted against. The four spec verbs correspond to
/// the four diff buckets; <c>OrphanedEdges</c> is the cascade side
/// effect of <c>RemoveNode</c>. Every scenario is one of the four
/// verbs so a regression in any single verb is isolated.
/// </summary>
public sealed class GraphPatchDiffComputerShould
{
    [Fact(DisplayName = "Given an AddNode patch, when the diff is computed, then AddedNodes contains the new node and no other bucket has entries")]
    public void AddNodeRendersInAddedNodesBucket()
    {
        var baseGraph = NewGraph(("intake", "intake"));
        var newNode = new ProcedureNode("verify", "verify", new Dictionary<string, string>());
        var patch = NewPatch(new GraphPatchOperation.AddNode(newNode));

        var diff = GraphPatchDiffComputer.Compute(baseGraph, patch);

        diff.AddedNodes.ShouldHaveSingleItem();
        diff.AddedNodes[0].ShouldBe(newNode);
        diff.RemovedNodeIds.ShouldBeEmpty();
        diff.RewiredEdges.ShouldBeEmpty();
        diff.OrphanedEdges.ShouldBeEmpty();
        diff.ReParameterizedNodes.ShouldBeEmpty();
        diff.HasChanges.ShouldBeTrue();
    }

    [Fact(DisplayName = "Given a RemoveNode patch, when the diff is computed, then the node id is in RemovedNodeIds and the touching edges are in OrphanedEdges")]
    public void RemoveNodeRendersInRemovedAndOrphanedBuckets()
    {
        var baseGraph = NewGraph(("intake", "intake"), ("verify", "verify"));
        baseGraph = baseGraph with { Edges = [new ProcedureEdge("intake", "accepted", "verify")] };

        var patch = NewPatch(new GraphPatchOperation.RemoveNode("verify"));

        var diff = GraphPatchDiffComputer.Compute(baseGraph, patch);

        diff.RemovedNodeIds.ShouldBe(["verify"]);
        diff.OrphanedEdges.ShouldHaveSingleItem();
        diff.OrphanedEdges[0].Edge.ShouldBe(new ProcedureEdge("intake", "accepted", "verify"));
        diff.AddedNodes.ShouldBeEmpty();
        diff.RewiredEdges.ShouldBeEmpty();
        diff.ReParameterizedNodes.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given a RewireEdge patch with a real Before and After, when the diff is computed, then RewiredEdges contains the transition with both endpoints named")]
    public void RewireEdgeRendersInRewiredEdgesBucket()
    {
        var baseGraph = NewGraph(("a", "intake"), ("b", "verify"));
        baseGraph = baseGraph with { Edges = [new ProcedureEdge("a", "accepted", "b")] };

        var before = new ProcedureEdge("a", "accepted", "b");
        var after = new ProcedureEdge("a", "rejected", "b");
        var patch = NewPatch(new GraphPatchOperation.RewireEdge(before, after));

        var diff = GraphPatchDiffComputer.Compute(baseGraph, patch);

        diff.RewiredEdges.ShouldHaveSingleItem();
        diff.RewiredEdges[0].Before.ShouldBe(before);
        diff.RewiredEdges[0].After.ShouldBe(after);
        diff.HasChanges.ShouldBeTrue();
    }

    [Fact(DisplayName = "Given a RewireEdge patch where Before == After, when the diff is computed, then the rewire is dropped (no diff entry)")]
    public void RewireEdgeNoOpIsDropped()
    {
        var baseGraph = NewGraph(("a", "intake"));
        baseGraph = baseGraph with { Edges = [new ProcedureEdge("a", "accepted", "self")] };

        var edge = new ProcedureEdge("a", "accepted", "self");
        var patch = NewPatch(new GraphPatchOperation.RewireEdge(edge, edge));

        var diff = GraphPatchDiffComputer.Compute(baseGraph, patch);

        diff.RewiredEdges.ShouldBeEmpty();
        diff.IsUnchanged.ShouldBeTrue();
    }

    [Fact(DisplayName = "Given a ReParameterizeNode patch with a changed parameter, when the diff is computed, then ReParameterizedNodes carries the per-parameter change")]
    public void ReParameterizeNodeRendersPerParameterDelta()
    {
        var baseGraph = NewGraphWithParams("verify", "verify", new Dictionary<string, string>
        {
            ["strictness"] = "low",
            ["timeout"] = "30s",
        });

        var newParams = new Dictionary<string, string>
        {
            ["strictness"] = "high",
            ["timeout"] = "30s",
            ["retries"] = "3",
        };
        var patch = NewPatch(new GraphPatchOperation.ReParameterizeNode("verify", newParams));

        var diff = GraphPatchDiffComputer.Compute(baseGraph, patch);

        diff.ReParameterizedNodes.ShouldHaveSingleItem();
        var entry = diff.ReParameterizedNodes[0];
        entry.NodeId.ShouldBe("verify");
        entry.Changes.Count.ShouldBe(2);

        var strictness = entry.Changes.Single(static change => change.Name == "strictness");
        strictness.Before.ShouldBe("low");
        strictness.After.ShouldBe("high");

        var retries = entry.Changes.Single(static change => change.Name == "retries");
        retries.Before.ShouldBeNull();
        retries.After.ShouldBe("3");
    }

    [Fact(DisplayName = "Given a ReParameterizeNode patch with identical parameters, when the diff is computed, then the op is dropped (no diff entry)")]
    public void ReParameterizeNodeIdenticalIsDropped()
    {
        var baseGraph = NewGraphWithParams("verify", "verify", new Dictionary<string, string>
        {
            ["strictness"] = "low",
        });

        var patch = NewPatch(new GraphPatchOperation.ReParameterizeNode(
            "verify",
            new Dictionary<string, string> { ["strictness"] = "low" }));

        var diff = GraphPatchDiffComputer.Compute(baseGraph, patch);

        diff.ReParameterizedNodes.ShouldBeEmpty();
        diff.IsUnchanged.ShouldBeTrue();
    }

    [Fact(DisplayName = "Given a composed patch touching all four verbs, when the diff is computed, then every bucket has its expected entry")]
    public void ComposedPatchRendersAllFourVerbs()
    {
        var baseGraph = NewGraphWithParams("intake", "intake", [])
            with
        {
            Nodes =
                [
                    new ProcedureNode("intake", "intake", new Dictionary<string, string>()),
                    new ProcedureNode("verify", "verify", new Dictionary<string, string> { ["strictness"] = "low" }),
                    new ProcedureNode("obsolete", "verify", new Dictionary<string, string>()),
                ],
            Edges =
                [
                    new ProcedureEdge("intake", "accepted", "verify"),
                    new ProcedureEdge("intake", "rejected", "obsolete"),
                ],
        };

        var newNode = new ProcedureNode("strict-verify", "verify", new Dictionary<string, string>());
        var patch = NewPatch(
            new GraphPatchOperation.AddNode(newNode),
            new GraphPatchOperation.RewireEdge(
                new ProcedureEdge("intake", "accepted", "verify"),
                new ProcedureEdge("intake", "accepted", "strict-verify")),
            new GraphPatchOperation.ReParameterizeNode(
                "verify",
                new Dictionary<string, string> { ["strictness"] = "high" }),
            new GraphPatchOperation.RemoveNode("obsolete"));

        var diff = GraphPatchDiffComputer.Compute(baseGraph, patch);

        diff.AddedNodes.ShouldHaveSingleItem();
        diff.AddedNodes[0].ShouldBe(newNode);
        diff.RewiredEdges.ShouldHaveSingleItem();
        diff.ReParameterizedNodes.ShouldHaveSingleItem();
        diff.ReParameterizedNodes[0].NodeId.ShouldBe("verify");
        diff.RemovedNodeIds.ShouldBe(["obsolete"]);
        diff.OrphanedEdges.ShouldHaveSingleItem();
        diff.OrphanedEdges[0].Edge.ShouldBe(new ProcedureEdge("intake", "rejected", "obsolete"));
    }

    [Fact(DisplayName = "Given a ReParameterizeNode that removes a parameter, when the diff is computed, then the change records a null After")]
    public void ReParameterizeNodeRecordsRemovedParameter()
    {
        var baseGraph = NewGraphWithParams("verify", "verify", new Dictionary<string, string>
        {
            ["strictness"] = "low",
            ["retries"] = "3",
        });

        var patch = NewPatch(new GraphPatchOperation.ReParameterizeNode(
            "verify",
            new Dictionary<string, string> { ["strictness"] = "low" }));

        var diff = GraphPatchDiffComputer.Compute(baseGraph, patch);

        diff.ReParameterizedNodes.ShouldHaveSingleItem();
        var entry = diff.ReParameterizedNodes[0];
        var removed = entry.Changes.Single();
        removed.Name.ShouldBe("retries");
        removed.Before.ShouldBe("3");
        removed.After.ShouldBeNull();
    }

    private static ProcedureGraph NewGraph(params (string Id, string KindKey)[] nodes)
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
