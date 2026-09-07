using Comuki.Engine.Orchestration.Domain.MergeQueue;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Orchestration.Unit.StatusMachine;

/// <summary>
/// Invariant guards of <see cref="MergeBatch"/>:
/// <see cref="MergeBatch.Create"/> rejects empty name / empty URL list /
/// empty URL entries; <see cref="MergeBatch.Claim"/>,
/// <see cref="MergeBatch.MarkMerged"/> and
/// <see cref="MergeBatch.MarkAbandoned"/> enforce the
/// <see cref="MergeBatchTransitions"/> table.
/// </summary>
public sealed class MergeBatchShould
{
    private static readonly DateTimeOffset now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given valid args, when Create is called, then the batch is Pending with the supplied urls")]
    public void CreatePendingBatch()
    {
        var urls = new[] { "https://example.com/pr/1", "https://example.com/pr/2" };

        var batch = MergeBatch.Create("release-train-q3", urls, now);

        batch.Status.ShouldBe(MergeBatchStatus.Pending);
        batch.Name.ShouldBe("release-train-q3");
        batch.PullRequestUrls.ShouldBe(urls);
        batch.CreatedAt.ShouldBe(now);
        batch.MergedAt.ShouldBeNull();
        batch.AbandonedAt.ShouldBeNull();
        batch.AbandonedReason.ShouldBeNull();
    }

    [Fact(DisplayName = "Given an empty name, when Create is called, then it throws")]
    public void RejectEmptyNameOnCreate()
    {
        Should.Throw<ArgumentException>(static () => MergeBatch.Create(
            " ",
            ["https://example.com/pr/1"],
            now));
    }

    [Fact(DisplayName = "Given an empty PR URL list, when Create is called, then it throws")]
    public void RejectEmptyUrlListOnCreate()
    {
        Should.Throw<ArgumentException>(static () => MergeBatch.Create(
            "release-train-q3",
            [],
            now));
    }

    [Fact(DisplayName = "Given a list with an empty URL, when Create is called, then it throws")]
    public void RejectEmptyUrlEntryOnCreate()
    {
        Should.Throw<ArgumentException>(static () => MergeBatch.Create(
            "release-train-q3",
            ["https://example.com/pr/1", " "],
            now));
    }

    [Fact(DisplayName = "Given a pending batch, when Claim is called, then it is InProgress")]
    public void ClaimPendingBatch()
    {
        var batch = PendingBatch();

        batch.Claim();

        batch.Status.ShouldBe(MergeBatchStatus.InProgress);
    }

    [Fact(DisplayName = "Given an in-progress batch, when MarkMerged is called, then it is Merged with timestamp")]
    public void MarkInProgressMerged()
    {
        var batch = PendingBatch();
        batch.Claim();
        var mergeAt = now.AddMinutes(2);

        batch.MarkMerged(mergeAt);

        batch.Status.ShouldBe(MergeBatchStatus.Merged);
        batch.MergedAt.ShouldBe(mergeAt);
    }

    [Fact(DisplayName = "Given a pending batch, when MarkMerged is called, then it throws")]
    public void RejectMergeFromPending()
    {
        var batch = PendingBatch();

        Should.Throw<InvalidOperationException>(() => batch.MarkMerged(now));
    }

    [Fact(DisplayName = "Given a pending batch, when MarkAbandoned is called, then it is Abandoned with reason")]
    public void AbandonPendingBatch()
    {
        var batch = PendingBatch();

        batch.MarkAbandoned("release train cancelled", now.AddMinutes(3));

        batch.Status.ShouldBe(MergeBatchStatus.Abandoned);
        batch.AbandonedAt.ShouldBe(now.AddMinutes(3));
        batch.AbandonedReason.ShouldBe("release train cancelled");
    }

    [Fact(DisplayName = "Given an in-progress batch, when MarkAbandoned is called, then it is Abandoned with reason")]
    public void AbandonInProgressBatch()
    {
        var batch = PendingBatch();
        batch.Claim();

        batch.MarkAbandoned("merge rejected by QA", now.AddMinutes(4));

        batch.Status.ShouldBe(MergeBatchStatus.Abandoned);
        batch.AbandonedReason.ShouldBe("merge rejected by QA");
    }

    [Fact(DisplayName = "Given a merged batch, when MarkAbandoned is called, then it throws (terminal status)")]
    public void RejectAbandonFromTerminal()
    {
        var batch = PendingBatch();
        batch.Claim();
        batch.MarkMerged(now.AddMinutes(1));

        Should.Throw<InvalidOperationException>(() => batch.MarkAbandoned("trying", now.AddMinutes(2)));
    }

    [Fact(DisplayName = "Given a merged batch, when Claim is called, then it throws (terminal status)")]
    public void RejectClaimFromMerged()
    {
        var batch = PendingBatch();
        batch.Claim();
        batch.MarkMerged(now.AddMinutes(1));

        Should.Throw<InvalidOperationException>(batch.Claim);
    }

    [Fact(DisplayName = "Given an abandoned batch, when Claim is called, then it throws")]
    public void RejectClaimFromAbandoned()
    {
        var batch = PendingBatch();
        batch.MarkAbandoned("dropped", now);

        Should.Throw<InvalidOperationException>(batch.Claim);
    }

    [Fact(DisplayName = "Given an empty reason, when MarkAbandoned is called, then it throws")]
    public void RejectEmptyReasonOnAbandon()
    {
        var batch = PendingBatch();

        Should.Throw<ArgumentException>(() => batch.MarkAbandoned(" ", now));
    }

    private static MergeBatch PendingBatch()
    {
        return MergeBatch.Create(
            "release-train-q3",
            ["https://example.com/pr/1"],
            now);
    }
}
