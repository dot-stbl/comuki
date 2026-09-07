using Comuki.Engine.Orchestration.Domain.MergeQueue;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Orchestration.Unit.StatusMachine;

/// <summary>
/// Invariant guards of <see cref="MergeQueueEntry"/>: <see cref="MergeQueueEntry.Create"/>
/// rejects empty fields; <see cref="MergeQueueEntry.Claim"/>,
/// <see cref="MergeQueueEntry.Release"/>, <see cref="MergeQueueEntry.MarkMerged"/>
/// and <see cref="MergeQueueEntry.MarkAbandoned"/> enforce the
/// <see cref="MergeQueueTransitions"/> table.
/// </summary>
public sealed class MergeQueueEntryShould
{
    private static readonly DateTimeOffset now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given valid args, when Create is called, then the entry is Pending and ids match")]
    public void CreatePendingEntry()
    {
        var entry = MergeQueueEntry.Create(
            ProjectId.New(),
            "feature/merge-queue",
            "https://github.com/comuki/comuki.orchestrator/pull/42",
            ConflictResolution.AutoRebase,
            "intake from #11",
            now);

        entry.Status.ShouldBe(MergeQueueStatus.Pending);
        entry.ConflictResolution.ShouldBe(ConflictResolution.AutoRebase);
        entry.EnqueuedAt.ShouldBe(now);
        entry.ClaimedBy.ShouldBeNull();
        entry.ClaimedAt.ShouldBeNull();
        entry.MergedAt.ShouldBeNull();
        entry.AbandonedAt.ShouldBeNull();
        entry.AbandonedReason.ShouldBeNull();
    }

    [Fact(DisplayName = "Given an empty branch, when Create is called, then it throws")]
    public void RejectEmptyBranchOnCreate()
    {
        Should.Throw<ArgumentException>(static () => MergeQueueEntry.Create(
            ProjectId.New(),
            " ",
            "https://example.com/pr/42",
            ConflictResolution.None,
            null,
            now));
    }

    [Fact(DisplayName = "Given an empty PR URL, when Create is called, then it throws")]
    public void RejectEmptyPullRequestUrlOnCreate()
    {
        Should.Throw<ArgumentException>(static () => MergeQueueEntry.Create(
            ProjectId.New(),
            "feature/x",
            " ",
            ConflictResolution.None,
            null,
            now));
    }

    [Fact(DisplayName = "Given a pending entry, when Claim is called, then it is InProgress with operator + timestamp")]
    public void ClaimPendingEntry()
    {
        var entry = PendingEntry();
        var claimAt = now.AddMinutes(1);

        entry.Claim("operator-alice", claimAt);

        entry.Status.ShouldBe(MergeQueueStatus.InProgress);
        entry.ClaimedBy.ShouldBe("operator-alice");
        entry.ClaimedAt.ShouldBe(claimAt);
    }

    [Fact(DisplayName = "Given an in-progress entry, when Release is called, then it returns to Pending and clears claim")]
    public void ReleaseInProgressEntry()
    {
        var entry = PendingEntry();
        entry.Claim("operator-bob", now);

        entry.Release();

        entry.Status.ShouldBe(MergeQueueStatus.Pending);
        entry.ClaimedBy.ShouldBeNull();
        entry.ClaimedAt.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a pending entry, when Release is called, then it throws")]
    public void RejectReleaseFromPending()
    {
        var entry = PendingEntry();

        Should.Throw<InvalidOperationException>(entry.Release);
    }

    [Fact(DisplayName = "Given an in-progress entry, when MarkMerged is called, then it is Merged with timestamp")]
    public void MarkInProgressMerged()
    {
        var entry = PendingEntry();
        entry.Claim("operator-carol", now);
        var mergeAt = now.AddMinutes(2);

        entry.MarkMerged(mergeAt);

        entry.Status.ShouldBe(MergeQueueStatus.Merged);
        entry.MergedAt.ShouldBe(mergeAt);
        entry.ClaimedBy.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a pending entry, when MarkMerged is called, then it throws")]
    public void RejectMergeFromPending()
    {
        var entry = PendingEntry();

        Should.Throw<InvalidOperationException>(() => entry.MarkMerged(now));
    }

    [Fact(DisplayName = "Given a pending entry, when MarkAbandoned is called, then it is Abandoned with reason")]
    public void AbandonPendingEntry()
    {
        var entry = PendingEntry();

        entry.MarkAbandoned("superseded by feature-X", now.AddMinutes(3));

        entry.Status.ShouldBe(MergeQueueStatus.Abandoned);
        entry.AbandonedAt.ShouldBe(now.AddMinutes(3));
        entry.AbandonedReason.ShouldBe("superseded by feature-X");
    }

    [Fact(DisplayName = "Given a merged entry, when MarkAbandoned is called, then it throws (terminal status)")]
    public void RejectAbandonFromTerminal()
    {
        var entry = PendingEntry();
        entry.Claim("operator-dan", now);
        entry.MarkMerged(now.AddMinutes(1));

        Should.Throw<InvalidOperationException>(() => entry.MarkAbandoned("trying", now.AddMinutes(2)));
    }

    [Fact(DisplayName = "Given an abandoned entry, when MarkMerged is called, then it throws")]
    public void RejectMergeFromAbandoned()
    {
        var entry = PendingEntry();
        entry.MarkAbandoned("dropped", now);

        Should.Throw<InvalidOperationException>(() => entry.MarkMerged(now.AddMinutes(1)));
    }

    [Fact(DisplayName = "Given any entry, when SetNotes is called, then the notes field is updated")]
    public void SetNotesOnAnyStatus()
    {
        var entry = PendingEntry();

        entry.SetNotes("first");
        entry.Notes.ShouldBe("first");

        entry.Claim("operator-eve", now);
        entry.SetNotes("second");
        entry.Notes.ShouldBe("second");
    }

    [Theory(DisplayName = "Given an empty operator id, when Claim is called, then it throws")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void RejectEmptyOperatorOnClaim(string? operatorId)
    {
        var entry = PendingEntry();

        Should.Throw<ArgumentException>(() => entry.Claim(operatorId!, now));
    }

    [Fact(DisplayName = "Given an empty reason, when MarkAbandoned is called, then it throws")]
    public void RejectEmptyReasonOnAbandon()
    {
        var entry = PendingEntry();

        Should.Throw<ArgumentException>(() => entry.MarkAbandoned(" ", now));
    }

    private static MergeQueueEntry PendingEntry()
    {
        return MergeQueueEntry.Create(
            ProjectId.New(),
            "feature/test",
            "https://example.com/pr/1",
            ConflictResolution.None,
            null,
            now);
    }
}
