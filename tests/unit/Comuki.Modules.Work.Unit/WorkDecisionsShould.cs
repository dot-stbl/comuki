using Comuki.Modules.Work.Domain;
using Comuki.Modules.Work.Domain.Completion;
using Comuki.Modules.Work.Domain.Exceptions;
using Comuki.Modules.Work.Domain.Sources;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Work.Unit;

/// <summary>
/// Aggregate-guard coverage for the Decision layer (tasks 5.x / 7.x / 8.x):
/// the deterministic handlers' pre-conditions on
/// <see cref="WorkTask"/>. Each test exercises a single Decision
/// transition's pre-condition and the post-condition the handler
/// relies on — the handler dispatch in
/// <c>WorkDecideHandler</c> is integration-covered by the
/// application-handler unit tests.
/// </summary>
public sealed class WorkDecisionsShould
{
    private static readonly ProjectId project = new(Guid.CreateVersion7());

    private static WorkTaskSourceRef PrimarySource()
    {
        return WorkTaskSourceRef.Primary(WorkTaskSourceKind.GitHub, "dot-stbl/comuki#89", "comuki#89");
    }

    private static WorkTask Blocked(DateTimeOffset now)
    {
        var task = WorkTask.Create(
            project,
            "title",
            "brief",
            PrimarySource(),
            WorkTaskCompletionPolicy.Default(now),
            now);
        task.TransitionTo(WorkTaskStatus.Ready, null, now);
        var runId = RunId.New();
        task.AppendAttempt(runId, now);
        task.CompleteAttempt(runId, now);
        task.TransitionTo(WorkTaskStatus.Blocked, null, now);
        return task;
    }

    [Fact(DisplayName = "Given a Blocked task, when Retry decision fires, then the status flips to Ready and no resolution outcome is set")]
    public void RetryFlipsBlockedToReady()
    {
        var now = DateTimeOffset.UtcNow;
        var task = Blocked(now);

        task.TransitionTo(WorkTaskStatus.Ready, null, now);

        task.Status.ShouldBe(WorkTaskStatus.Ready);
        task.ResolutionOutcome.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a Blocked task, when Waiver decision fires, then the status flips to Resolved with Waived outcome")]
    public void WaiverFlipsBlockedToResolvedWithWaived()
    {
        var now = DateTimeOffset.UtcNow;
        var task = Blocked(now);

        task.TransitionTo(WorkTaskStatus.Resolved, WorkTaskResolutionOutcome.Waived, now);

        task.Status.ShouldBe(WorkTaskStatus.Resolved);
        task.ResolutionOutcome.ShouldBe(WorkTaskResolutionOutcome.Waived);
    }

    [Fact(DisplayName = "Given a Blocked task, when FailedResolution decision fires, then the status flips to Resolved with Failed outcome")]
    public void FailedResolutionFlipsBlockedToResolvedWithFailed()
    {
        var now = DateTimeOffset.UtcNow;
        var task = Blocked(now);

        task.TransitionTo(WorkTaskStatus.Resolved, WorkTaskResolutionOutcome.Failed, now);

        task.Status.ShouldBe(WorkTaskStatus.Resolved);
        task.ResolutionOutcome.ShouldBe(WorkTaskResolutionOutcome.Failed);
    }

    [Fact(DisplayName = "Given a Blocked task, when Cancellation decision fires, then the status flips to Cancelled")]
    public void CancellationFlipsBlockedToCancelled()
    {
        var now = DateTimeOffset.UtcNow;
        var task = Blocked(now);

        task.TransitionTo(WorkTaskStatus.Cancelled, null, now);

        task.Status.ShouldBe(WorkTaskStatus.Cancelled);
    }

    [Fact(DisplayName = "Given an active attempt, when Cancellation decision fires, then the attempt is cleared in the same transition path")]
    public void CancellationClearsActiveAttempt()
    {
        var now = DateTimeOffset.UtcNow;
        var task = Blocked(now);
        // Blocked implies no active attempt; create a second attempt path
        // by going Ready → append → Blocked.
        task.TransitionTo(WorkTaskStatus.Ready, null, now);
        var runId = RunId.New();
        task.AppendAttempt(runId, now);

        task.TransitionTo(WorkTaskStatus.Cancelled, null, now);

        task.Status.ShouldBe(WorkTaskStatus.Cancelled);
        task.HasActiveAttempt.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a non-terminal task, when StampReplacementOutcome fires, then the task resolves with Replaced outcome")]
    public void StampReplacementOutcomeResolvesWithReplaced()
    {
        var now = DateTimeOffset.UtcNow;
        var task = Blocked(now);

        task.StampReplacementOutcome(now);

        task.Status.ShouldBe(WorkTaskStatus.Resolved);
        task.ResolutionOutcome.ShouldBe(WorkTaskResolutionOutcome.Replaced);
    }

    [Fact(DisplayName = "Given a terminal task, when StampReplacementOutcome fires, then it throws")]
    public void StampReplacementOnTerminalThrows()
    {
        var now = DateTimeOffset.UtcNow;
        var task = Blocked(now);
        task.TransitionTo(WorkTaskStatus.Resolved, WorkTaskResolutionOutcome.Waived, now);

        Should.Throw<WorkTaskDomainException>(
            () => task.StampReplacementOutcome(now));
    }
}
