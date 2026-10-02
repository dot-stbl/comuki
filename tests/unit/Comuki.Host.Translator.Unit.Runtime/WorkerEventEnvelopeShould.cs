using Comuki.Host.Translator.Execution.Loop;
using Shouldly;
using Xunit;

namespace Comuki.Host.Translator.Unit.Runtime;

/// <summary>
/// Builders on <see cref="WorkerEventEnvelope"/>: the Start / Report /
/// Condition envelopes the Translator sends over the worker gRPC stream
/// (harden-pi-worker-sandbox 5.1, spec D6). Conditions carry the
/// <c>{workItemId, name, value}</c> shape the host journals as
/// <c>worker.condition</c> entries.
/// </summary>
public sealed class WorkerEventEnvelopeShould
{
    [Fact(DisplayName = "Given a work item id and condition name/value, when ToConditionEvent is built, then the event carries the StageCondition payload")]
    public void ConditionEventCarriesPayload()
    {
        var workItemId = Guid.NewGuid();

        var @event = WorkerEventEnvelope.ToConditionEvent(workItemId, "WorkspacePrepared", value: true);

        @event.Condition.ShouldNotBeNull();
        @event.Condition!.WorkItemId.ShouldBe(workItemId.ToString());
        @event.Condition.Name.ShouldBe("WorkspacePrepared");
        @event.Condition.Value.ShouldBeTrue();
    }

    [Fact(DisplayName = "Given a false condition, when ToConditionEvent is built, then the value is preserved as false")]
    public void ConditionEventPreservesFalse()
    {
        var workItemId = Guid.NewGuid();

        var @event = WorkerEventEnvelope.ToConditionEvent(workItemId, "EgressApplied", value: false);

        @event.Condition.ShouldNotBeNull();
        @event.Condition!.Value.ShouldBeFalse();
        @event.Condition.Name.ShouldBe("EgressApplied");
    }

    [Fact(DisplayName = "Given the three sandbox conditions, when ToConditionEvent is built for each, then each carries its documented name")]
    public void AllThreeConditionsRoundTrip()
    {
        var workItemId = Guid.NewGuid();

        var names = new[] { "WorkspacePrepared", "EgressApplied", "AgentRunning" };
        foreach (var name in names)
        {
            var @event = WorkerEventEnvelope.ToConditionEvent(workItemId, name, value: true);

            @event.Condition.ShouldNotBeNull();
            @event.Condition!.Name.ShouldBe(name);
            @event.Condition.WorkItemId.ShouldBe(workItemId.ToString());
            @event.Condition.Value.ShouldBeTrue();
        }
    }

    [Fact(DisplayName = "Given a condition event, when the envelope is built, then it has no other stage payload set")]
    public void ConditionEventHasNoOtherStage()
    {
        var workItemId = Guid.NewGuid();

        var @event = WorkerEventEnvelope.ToConditionEvent(workItemId, "AgentRunning", value: true);

        @event.Start.ShouldBeNull();
        @event.Activity.ShouldBeNull();
        @event.Report.ShouldBeNull();
        @event.Verify.ShouldBeNull();
        @event.Condition.ShouldNotBeNull();
    }
}
