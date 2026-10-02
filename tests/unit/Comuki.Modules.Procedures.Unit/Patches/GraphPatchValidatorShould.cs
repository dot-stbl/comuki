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
/// Unit tests for task 3.1: the GraphPatch validator refuses patches
/// that cannot be applied to their base graph (missing nodes, missing
/// edges, colliding ids) before the applier is reached. Every refusal
/// names the offending element so Studio and the chat surface can
/// surface the reason without a re-run.
/// </summary>
public sealed class GraphPatchValidatorShould
{
    [Fact(DisplayName = "Given a patch with AddNode that collides with a base id, when validated, then the refusal names the colliding id")]
    public void AddNodeCollisionIsRefused()
    {
        var baseGraph = NewGraph(("intake", "intake"));
        var patch = NewPatch(new GraphPatchOperation.AddNode(new ProcedureNode("intake", "verify", new Dictionary<string, string>())));

        var exception = Should.Throw<GraphPatchException>(
            () => GraphPatchValidator.Validate(baseGraph, patch));

        exception.Code.ShouldBe(GraphPatchException.NodeAlreadyExists);
        exception.Message.ShouldContain("intake");
    }

    [Fact(DisplayName = "Given a patch with RemoveNode on an absent id, when validated, then the refusal names the missing id")]
    public void RemoveNodeOnMissingIdIsRefused()
    {
        var baseGraph = NewGraph(("intake", "intake"));
        var patch = NewPatch(new GraphPatchOperation.RemoveNode("ghost"));

        var exception = Should.Throw<GraphPatchException>(
            () => GraphPatchValidator.Validate(baseGraph, patch));

        exception.Code.ShouldBe(GraphPatchException.NodeNotFound);
        exception.Message.ShouldContain("ghost");
    }

    [Fact(DisplayName = "Given a patch with RemoveNode that follows an AddNode of the same id, when validated, then the patch is accepted")]
    public void RemoveNodeAfterAddNodeIsAccepted()
    {
        var baseGraph = NewGraph();
        var patch = NewPatch(
            new GraphPatchOperation.AddNode(new ProcedureNode("transient", "intake", new Dictionary<string, string>())),
            new GraphPatchOperation.RemoveNode("transient"));

        Should.NotThrow(() => GraphPatchValidator.Validate(baseGraph, patch));
    }

    [Fact(DisplayName = "Given a patch with RewireEdge whose Before is absent, when validated, then the refusal names the missing edge tuple")]
    public void RewireEdgeBeforeMissingIsRefused()
    {
        var baseGraph = NewGraph(("a", "intake"), ("b", "verify"));
        baseGraph = baseGraph with { Edges = [new ProcedureEdge("a", "accepted", "b")] };

        var patch = NewPatch(new GraphPatchOperation.RewireEdge(
            new ProcedureEdge("a", "rejected", "b"),
            new ProcedureEdge("a", "accepted", "b")));

        var exception = Should.Throw<GraphPatchException>(
            () => GraphPatchValidator.Validate(baseGraph, patch));

        exception.Code.ShouldBe(GraphPatchException.EdgeNotFound);
        exception.Message.ShouldContain("rejected");
    }

    [Fact(DisplayName = "Given a patch with RewireEdge whose After collides with an existing edge, when validated, then the refusal names the colliding edge tuple")]
    public void RewireEdgeAfterCollisionIsRefused()
    {
        var baseGraph = NewGraph(("a", "intake"), ("b", "verify"), ("c", "verify"));
        baseGraph = baseGraph with
        {
            Edges =
            [
                new ProcedureEdge("a", "accepted", "b"),
                new ProcedureEdge("a", "accepted", "c"),
            ],
        };

        var patch = NewPatch(new GraphPatchOperation.RewireEdge(
            new ProcedureEdge("a", "accepted", "b"),
            new ProcedureEdge("a", "accepted", "c")));

        var exception = Should.Throw<GraphPatchException>(
            () => GraphPatchValidator.Validate(baseGraph, patch));

        exception.Code.ShouldBe(GraphPatchException.EdgeAlreadyExists);
    }

    [Fact(DisplayName = "Given a patch with ReParameterizeNode on an absent id, when validated, then the refusal names the missing id")]
    public void ReParameterizeOnMissingIdIsRefused()
    {
        var baseGraph = NewGraph(("intake", "intake"));
        var patch = NewPatch(new GraphPatchOperation.ReParameterizeNode("ghost", new Dictionary<string, string> { ["x"] = "1" }));

        var exception = Should.Throw<GraphPatchException>(
            () => GraphPatchValidator.Validate(baseGraph, patch));

        exception.Code.ShouldBe(GraphPatchException.NodeNotFound);
        exception.Message.ShouldContain("ghost");
    }

    [Fact(DisplayName = "Given a well-formed patch, when validated, then no exception is raised")]
    public void WellFormedPatchIsAccepted()
    {
        var baseGraph = NewGraph(("intake", "intake"), ("verify", "verify"));
        baseGraph = baseGraph with { Edges = [new ProcedureEdge("intake", "accepted", "verify")] };

        var patch = NewPatch(
            new GraphPatchOperation.RewireEdge(
                new ProcedureEdge("intake", "accepted", "verify"),
                new ProcedureEdge("intake", "rejected", "verify")),
            new GraphPatchOperation.ReParameterizeNode("verify", new Dictionary<string, string> { ["strictness"] = "high" }));

        Should.NotThrow(() => GraphPatchValidator.Validate(baseGraph, patch));
    }

    private static ProcedureGraph NewGraph(params (string Id, string KindKey)[] nodes)
    {
        var nodeList = nodes
            .Select(static spec => new ProcedureNode(spec.Id, spec.KindKey, new Dictionary<string, string>()))
            .ToList();
        return new ProcedureGraph(nodeList, []);
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
