using Comuki.AgentTest.Runner.Journal;
using Comuki.AgentTest.Runner.Scenarios;
using Comuki.Shared.Contracts.Journal;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.AgentTest.Runner.Unit;

/// <summary>
/// Proves the T2a journal-condition vocabulary against synthetic
/// <see cref="RunEventEntry"/> timelines — no database, no container.
/// </summary>
public sealed class JournalConditionEvaluatorShould
{
    private static readonly RunId someRunId = new(Guid.NewGuid());

    [Fact(DisplayName = "Given a timeline with a Queued->Running transition, when WorkspacePrepared is evaluated, then it is true")]
    public void EvaluateWorkspacePreparedTrueOnClaimTransition()
    {
        var timeline = new[]
        {
            Entry("work_item.status_changed", /*lang=json,strict*/ """{"from":"Queued","to":"Running"}"""),
        };

        JournalConditionEvaluator.Evaluate(JournalConditionEvaluator.WorkspacePrepared, timeline).ShouldBeTrue();
    }

    [Fact(DisplayName = "Given a timeline with no status change, when WorkspacePrepared is evaluated, then it is false")]
    public void EvaluateWorkspacePreparedFalseWithNoClaim()
    {
        var timeline = Array.Empty<RunEventEntry>();

        JournalConditionEvaluator.Evaluate(JournalConditionEvaluator.WorkspacePrepared, timeline).ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a timeline with a worker.reported entry, when AgentRunning is evaluated, then it is true")]
    public void EvaluateAgentRunningTrueWithWorkerReported()
    {
        var timeline = new[]
        {
            Entry("worker.reported", /*lang=json,strict*/ """{"brief":"do the thing"}"""),
        };

        JournalConditionEvaluator.Evaluate(JournalConditionEvaluator.AgentRunning, timeline).ShouldBeTrue();
    }

    [Fact(DisplayName = "Given a timeline with only status-change entries, when AgentRunning is evaluated, then it is false")]
    public void EvaluateAgentRunningFalseWithoutWorkerReported()
    {
        var timeline = new[]
        {
            Entry("work_item.status_changed", /*lang=json,strict*/ """{"from":"Queued","to":"Running"}"""),
            Entry("work_item.status_changed", /*lang=json,strict*/ """{"from":"Running","to":"Succeeded"}"""),
        };

        JournalConditionEvaluator.Evaluate(JournalConditionEvaluator.AgentRunning, timeline).ShouldBeFalse();
    }

    [Fact(DisplayName = "Given an unknown condition name, when evaluated, then it fails naming the condition and the known vocabulary")]
    public void ThrowOnUnknownCondition()
    {
        var exception = Should.Throw<ScenarioValidationException>(
            static () => JournalConditionEvaluator.Evaluate("NotARealCondition", []));

        exception.Message.ShouldContain("NotARealCondition");
        exception.Message.ShouldContain(JournalConditionEvaluator.WorkspacePrepared);
        exception.Message.ShouldContain(JournalConditionEvaluator.AgentRunning);
    }

    [Theory(DisplayName = "Given a condition name in any case, when evaluated, then case does not matter")]
    [InlineData("workspaceprepared")]
    [InlineData("WORKSPACEPREPARED")]
    [InlineData("WorkspacePrepared")]
    public void MatchConditionNamesCaseInsensitively(string condition)
    {
        var timeline = new[] { Entry("work_item.status_changed", /*lang=json,strict*/ """{"to":"Running"}""") };

        JournalConditionEvaluator.Evaluate(condition, timeline).ShouldBeTrue();
    }

    private static RunEventEntry Entry(string type, string payloadJson)
    {
        return new RunEventEntry(Guid.NewGuid(), someRunId, type, payloadJson, DateTimeOffset.UtcNow);
    }
}
