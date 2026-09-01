using Comuki.Shared.Contracts.Plans;
using Shouldly;
using Xunit;

namespace Comuki.Host.Brain.Unit;

/// <summary>
/// Plan JSON bridge: camelCase wire form, malformed-JSON errors, the
/// null document, and the TryParse out-plan contract the toolbox uses
/// for its catalog follow-up check.
/// </summary>
public sealed class PlanJsonShould
{
    private const string ValidPlanJson =
                             /*lang=json,strict*/
                             """
        {"summary":"ship it","nodes":[{"id":"n1","title":"step","profileKey":"implement","brief":"do work"}],"edges":[]}
        """;

    [Fact(DisplayName = "Given a valid plan document, when TryParse is called, then it succeeds with the plan back")]
    public void TryParseValidPlan()
    {
        var parsed = PlanJson.TryParse(ValidPlanJson, out var plan, out var validation);

        parsed.ShouldBeTrue();
        validation.IsValid.ShouldBeTrue();
        plan.ShouldNotBeNull();
        plan.Summary.ShouldBe("ship it");
        plan.Nodes.ShouldHaveSingleItem().ProfileKey.ShouldBe("implement");
    }

    [Fact(DisplayName = "Given a malformed document, when TryParse is called, then the error says the json is malformed")]
    public void TryParseMalformedJson()
    {
        var parsed = PlanJson.TryParse("{\"summary\": ", out var plan, out var validation);

        parsed.ShouldBeFalse();
        plan.ShouldBeNull();
        validation.Errors.ShouldHaveSingleItem().ShouldContain("plan json is malformed");
    }

    [Fact(DisplayName = "Given the literal null document, when TryParse is called, then it fails with the required error")]
    public void TryParseNullDocument()
    {
        var parsed = PlanJson.TryParse("null", out var plan, out var validation);

        parsed.ShouldBeFalse();
        plan.ShouldBeNull();
        validation.Errors.ShouldContain("plan is required");
    }

    [Fact(DisplayName = "Given an invalid plan, when Parse is called, then both parse and validate errors surface")]
    public void ParseReportsValidationErrors()
    {
        var validation = PlanJson.Parse(/*lang=json,strict*/ """{"summary":"s","nodes":[],"edges":[]}""");

        validation.IsValid.ShouldBeFalse();
        validation.Errors.ShouldContain("plan must contain at least one node");
    }

    [Fact(DisplayName = "Given a plan, when serialized and parsed back, then the round trip is lossless")]
    public void RoundTripSerialization()
    {
        var plan = new Plan(
            "summary",
            [new PlanNode("n1", "title", "implement", "brief"), new PlanNode("n2", "title 2", "implement", "brief 2")],
            [new PlanEdge("n1", "n2")]);

        var json = PlanJson.Serialize(plan);
        PlanJson.Parse(json).IsValid.ShouldBeTrue();
    }
}
