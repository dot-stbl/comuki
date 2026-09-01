using Comuki.Shared.Contracts.Plans;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Chat.Unit;

/// <summary>
/// Structural plan gate: the shapes the chat graph accepts and rejects
/// before any approve card is shown (issue #5 slice B).
/// </summary>
public sealed class PlanValidatorShould
{
    private readonly PlanValidator validator = new();

    [Fact(DisplayName = "Given a single-item plan, when validated, then it passes")]
    public void AcceptSingleItemPlan()
    {
        var plan = new Plan([new PlanItem("step-1", "implement", "do the thing", [])]);

        validator.Validate(plan).IsValid.ShouldBeTrue();
    }

    [Fact(DisplayName = "Given a diamond DAG, when validated, then it passes")]
    public void AcceptDiamondDag()
    {
        var plan = new Plan(
        [
            new PlanItem("a", "explore-readonly", "explore", []),
            new PlanItem("b", "implement", "left", ["a"]),
            new PlanItem("c", "implement", "right", ["a"]),
            new PlanItem("d", "verify", "join", ["b", "c"]),
        ]);

        validator.Validate(plan).IsValid.ShouldBeTrue();
    }

    [Fact(DisplayName = "Given an empty plan, when validated, then it fails")]
    public void RejectEmptyPlan()
    {
        validator.Validate(new Plan([])).IsValid.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given duplicate keys, when validated, then it fails")]
    public void RejectDuplicateKeys()
    {
        var plan = new Plan(
        [
            new PlanItem("a", "implement", "one", []),
            new PlanItem("a", "implement", "two", []),
        ]);

        validator.Validate(plan).IsValid.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a dependsOn key that does not exist, when validated, then it fails")]
    public void RejectUnknownDependency()
    {
        var plan = new Plan([new PlanItem("a", "implement", "one", ["ghost"])]);

        validator.Validate(plan).IsValid.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a self-dependency, when validated, then it fails")]
    public void RejectSelfDependency()
    {
        var plan = new Plan([new PlanItem("a", "implement", "one", ["a"])]);

        validator.Validate(plan).IsValid.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a dependency cycle, when validated, then it fails")]
    public void RejectCycle()
    {
        var plan = new Plan(
        [
            new PlanItem("a", "implement", "one", ["b"]),
            new PlanItem("b", "implement", "two", ["c"]),
            new PlanItem("c", "implement", "three", ["a"]),
        ]);

        validator.Validate(plan).IsValid.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given an item without a profile key, when validated, then it fails")]
    public void RejectMissingProfileKey()
    {
        var plan = new Plan([new PlanItem("a", " ", "one", [])]);

        validator.Validate(plan).IsValid.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given more items than the slop guard allows, when validated, then it fails")]
    public void RejectTooManyItems()
    {
        var items = Enumerable.Range(0, PlanValidator.MaxItems + 1)
            .Select(static index => new PlanItem("step-" + index, "implement", "brief", []))
            .ToList();

        validator.Validate(new Plan(items)).IsValid.ShouldBeFalse();
    }
}
