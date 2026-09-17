using Comuki.Modules.Memory.Domain.Learning;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Memory.Unit;

/// <summary>
/// Learning-candidate lifecycle: first sighting, repeat counter, the
/// one-decision guard on approve/reject, and the reject reason.
/// </summary>
public sealed class LearningCandidateShould
{
    private static readonly DateTimeOffset now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid projectId = Guid.NewGuid();

    [Fact(DisplayName = "Given a first sighting, when Create is called, then the candidate is pending with one repeat")]
    public void CreatePendingWithFirstSighting()
    {
        var candidate = LearningCandidate.Create(
            projectId, "build.dotnet", "tests failed on cold cache", "run tests before merge", "worker:1", now);

        candidate.RepeatCount.ShouldBe(1);
        candidate.Status.ShouldBe(LearningStatus.Pending);
        candidate.ProjectId.ShouldBe(projectId);
        candidate.Topic.ShouldBe("build.dotnet");
        candidate.Observation.ShouldBe("tests failed on cold cache");
        candidate.ProposedRule.ShouldBe("run tests before merge");
        candidate.SourceRef.ShouldBe("worker:1");
        candidate.DecidedAt.ShouldBeNull();
        candidate.DecisionReason.ShouldBeNull();
    }

    [Theory(DisplayName = "Given an empty field, when Create is called, then ArgumentException names it")]
    [InlineData("projectId")]
    [InlineData("topic")]
    [InlineData("observation")]
    [InlineData("proposedRule")]
    [InlineData("sourceRef")]
    public void RefuseEmptyFields(string paramName)
    {
        var exception = Should.Throw<ArgumentException>(() => LearningCandidate.Create(
            paramName == "projectId" ? Guid.Empty : projectId,
            paramName == "topic" ? " " : "build.dotnet",
            paramName == "observation" ? "" : "observed",
            paramName == "proposedRule" ? " " : "rule text",
            paramName == "sourceRef" ? "" : "source ref",
            now));

        exception.ParamName.ShouldBe(paramName);
    }

    [Fact(DisplayName = "Given repeated sightings, when RegisterRepeat is called, then the counter grows")]
    public void CountRepeats()
    {
        var candidate = NewCandidate();

        candidate.RegisterRepeat();
        candidate.RegisterRepeat();

        candidate.RepeatCount.ShouldBe(3);
    }

    [Fact(DisplayName = "Given a pending candidate, when approved, then status and decision time are recorded")]
    public void ApprovePendingCandidate()
    {
        var candidate = NewCandidate();
        var decidedAt = now.AddHours(1);

        candidate.Approve(decidedAt);

        candidate.Status.ShouldBe(LearningStatus.Approved);
        candidate.DecidedAt.ShouldBe(decidedAt);
    }

    [Fact(DisplayName = "Given a pending candidate, when rejected with a reason, then the reason is kept trimmed")]
    public void RejectKeepsTheHumanReason()
    {
        var candidate = NewCandidate();
        var decidedAt = now.AddHours(1);

        candidate.Reject(decidedAt, "  already covered by the build rules  ");

        candidate.Status.ShouldBe(LearningStatus.Rejected);
        candidate.DecidedAt.ShouldBe(decidedAt);
        candidate.DecisionReason.ShouldBe("already covered by the build rules");
    }

    [Fact(DisplayName = "Given a pending candidate, when rejected without a reason, then the reason stays null")]
    public void RejectWithoutReasonStaysNull()
    {
        var candidate = NewCandidate();

        candidate.Reject(now);

        candidate.DecisionReason.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a decided candidate, when decided again, then InvalidOperationException refuses it")]
    public void RefuseSecondDecision()
    {
        var candidate = NewCandidate();
        candidate.Reject(now);

        Should.Throw<InvalidOperationException>(() => candidate.Approve(now.AddMinutes(1)));
        Should.Throw<InvalidOperationException>(() => candidate.Reject(now.AddMinutes(1)));
    }

    private static LearningCandidate NewCandidate()
    {
        return LearningCandidate.Create(projectId, "build.dotnet", "observed", "rule text", "source ref", now);
    }
}
