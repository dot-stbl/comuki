using Comuki.Modules.Memory.Domain.Learning;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Memory.Unit;

/// <summary>
/// Learning-candidate lifecycle: first sighting, repeat counter, and the
/// one-decision guard on approve/reject.
/// </summary>
public sealed class LearningCandidateShould
{
    private static readonly DateTimeOffset now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given a first sighting, when Create is called, then the candidate is pending with one repeat")]
    public void CreatePendingWithFirstSighting()
    {
        var candidate = LearningCandidate.Create("run tests before merge", "pr-42 review comment", now);

        candidate.RepeatCount.ShouldBe(1);
        candidate.Status.ShouldBe(LearningStatus.Pending);
        candidate.DecidedAt.ShouldBeNull();
        candidate.Pattern.ShouldBe("run tests before merge");
        candidate.SourceRef.ShouldBe("pr-42 review comment");
    }

    [Theory(DisplayName = "Given an empty field, when Create is called, then ArgumentException names it")]
    [InlineData("pattern")]
    [InlineData("sourceRef")]
    public void RefuseEmptyFields(string paramName)
    {
        var exception = Should.Throw<ArgumentException>(() => LearningCandidate.Create(
            paramName == "pattern" ? " " : "pattern text",
            paramName == "sourceRef" ? "" : "source ref",
            now));

        exception.ParamName.ShouldBe(paramName);
    }

    [Fact(DisplayName = "Given repeated sightings, when RegisterRepeat is called, then the counter grows")]
    public void CountRepeats()
    {
        var candidate = LearningCandidate.Create("pattern", "ref", now);

        candidate.RegisterRepeat();
        candidate.RegisterRepeat();

        candidate.RepeatCount.ShouldBe(3);
    }

    [Fact(DisplayName = "Given a pending candidate, when approved, then status and decision time are recorded")]
    public void ApprovePendingCandidate()
    {
        var candidate = LearningCandidate.Create("pattern", "ref", now);
        var decidedAt = now.AddHours(1);

        candidate.Approve(decidedAt);

        candidate.Status.ShouldBe(LearningStatus.Approved);
        candidate.DecidedAt.ShouldBe(decidedAt);
    }

    [Fact(DisplayName = "Given a decided candidate, when decided again, then InvalidOperationException refuses it")]
    public void RefuseSecondDecision()
    {
        var candidate = LearningCandidate.Create("pattern", "ref", now);
        candidate.Reject(now);

        Should.Throw<InvalidOperationException>(() => candidate.Approve(now.AddMinutes(1)));
        Should.Throw<InvalidOperationException>(() => candidate.Reject(now.AddMinutes(1)));
    }
}
