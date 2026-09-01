using Comuki.Shared.Contracts.Plans;
using Shouldly;
using Xunit;

namespace Comuki.Host.Brain.Unit;

/// <summary>
/// Plan validation rules the brain's emit_plan relies on: shape checks,
/// referential integrity, self-loops and DAG acyclicity (S5 contract:
/// nodes non-empty, acyclic dependsOn).
/// </summary>
public sealed class PlanValidatorShould
{
    [Fact(DisplayName = "Given a well-formed plan, when validated, then it is valid")]
    public void AcceptWellFormedPlan()
    {
        var plan = new Plan(
            "ship the slice",
            [Node("n1"), Node("n2")],
            [new PlanEdge("n1", "n2")]);

        var result = PlanValidator.Validate(plan);

        result.IsValid.ShouldBeTrue();
        result.Errors.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given null, when validated, then the error says the plan is required")]
    public void RefuseNullPlan()
    {
        var result = PlanValidator.Validate(null);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain("plan is required");
    }

    [Fact(DisplayName = "Given no nodes, when validated, then the error demands at least one node")]
    public void RefuseEmptyNodes()
    {
        var result = PlanValidator.Validate(new Plan("summary", [], []));

        result.Errors.ShouldContain("plan must contain at least one node");
    }

    [Theory(DisplayName = "Given a node with an empty field, when validated, then the error names the node")]
    [InlineData("id")]
    [InlineData("title")]
    [InlineData("profileKey")]
    [InlineData("brief")]
    public void RefuseEmptyNodeFields(string field)
    {
        var node = new PlanNode(
            field == "id" ? " " : "n1",
            field == "title" ? "" : "title",
            field == "profileKey" ? "  " : "implement",
            field == "brief" ? "" : "do the work");

        var result = PlanValidator.Validate(new Plan("summary", [node], []));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(
            field == "id"
                ? "node id must not be empty"
                : field == "profileKey"
                    ? "node 'n1' profile key must not be empty"
                    : $"node 'n1' {field} must not be empty");
    }

    [Fact(DisplayName = "Given duplicate node ids, when validated, then the error names the duplicate")]
    public void RefuseDuplicateNodeIds()
    {
        var result = PlanValidator.Validate(new Plan("summary", [Node("n1"), Node("n1")], []));

        result.Errors.ShouldContain("node id 'n1' is duplicated");
    }

    [Fact(DisplayName = "Given an edge to an unknown node, when validated, then the error names it")]
    public void RefuseEdgesToUnknownNodes()
    {
        var result = PlanValidator.Validate(new Plan("summary", [Node("n1")], [new PlanEdge("n1", "ghost")]));

        result.Errors.ShouldContain("edge references unknown node 'ghost'");
    }

    [Fact(DisplayName = "Given a self-loop edge, when validated, then the error calls it out")]
    public void RefuseSelfLoops()
    {
        var result = PlanValidator.Validate(new Plan("summary", [Node("n1")], [new PlanEdge("n1", "n1")]));

        result.Errors.ShouldContain("edge 'n1' -> 'n1' is a self-loop");
    }

    [Fact(DisplayName = "Given a cyclic graph, when validated, then the error reports the cycle")]
    public void RefuseCyclicGraph()
    {
        var plan = new Plan(
            "summary",
            [Node("n1"), Node("n2"), Node("n3")],
            [new PlanEdge("n1", "n2"), new PlanEdge("n2", "n3"), new PlanEdge("n3", "n1")]);

        var result = PlanValidator.Validate(plan);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain("plan graph must be acyclic (cycle passes through 'n1')");
    }

    [Fact(DisplayName = "Given a diamond DAG, when validated, then it is accepted")]
    public void AcceptDiamondDag()
    {
        var plan = new Plan(
            "summary",
            [Node("n1"), Node("n2"), Node("n3"), Node("n4")],
            [
                new PlanEdge("n1", "n2"),
                new PlanEdge("n1", "n3"),
                new PlanEdge("n2", "n4"),
                new PlanEdge("n3", "n4"),
            ]);

        PlanValidator.Validate(plan).IsValid.ShouldBeTrue();
    }

    [Fact(DisplayName = "Given an empty summary, when validated, then the error demands one")]
    public void RefuseEmptySummary()
    {
        var result = PlanValidator.Validate(new Plan("", [Node("n1")], []));

        result.Errors.ShouldContain("summary must not be empty");
    }

    private static PlanNode Node(string id)
    {
        return new PlanNode(id, $"title {id}", "implement", $"brief {id}");
    }
}
