using Comuki.Engine.Orchestration.Application.MergeQueue;
using Comuki.Engine.Orchestration.Application.MergeQueue.Abandon;
using Comuki.Engine.Orchestration.Application.MergeQueue.Annotate;
using Comuki.Engine.Orchestration.Application.MergeQueue.Claim;
using Comuki.Engine.Orchestration.Application.MergeQueue.MergeEntry;
using Comuki.Engine.Orchestration.Application.MergeQueue.Release;
using Comuki.Engine.Orchestration.Domain.MergeQueue;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Orchestration.Unit.StatusMachine;

/// <summary>
/// Structural validation of <see cref="EnqueueMergeRequestCommand"/>: non-empty
/// branch / URL, bounded string lengths.
/// </summary>
public sealed class MergeQueueValidatorShould
{
    private readonly MergeQueueValidator validator = new();

    [Fact(DisplayName = "Given a well-formed command, when validated, then it passes")]
    public void AcceptWellFormedCommand()
    {
        var command = new EnqueueMergeRequestCommand(
            ProjectId.New(),
            "feature/merge-queue",
            "https://github.com/comuki/comuki.orchestrator/pull/42",
            ConflictResolution.AutoRebase,
            "intake from #11");

        var result = validator.Validate(command);

        result.IsValid.ShouldBeTrue();
    }

    [Theory(DisplayName = "Given an empty branch, when validated, then it fails")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void RefuseEmptyBranch(string? branchName)
    {
        var command = new EnqueueMergeRequestCommand(
            ProjectId.New(),
            branchName!,
            "https://example.com/pr/1",
            ConflictResolution.None,
            null);

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.PropertyName == "BranchName");
    }

    [Theory(DisplayName = "Given an empty PR URL, when validated, then it fails")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void RefuseEmptyPullRequestUrl(string? url)
    {
        var command = new EnqueueMergeRequestCommand(
            ProjectId.New(),
            "feature/x",
            url!,
            ConflictResolution.None,
            null);

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.PropertyName == "PullRequestUrl");
    }

    [Fact(DisplayName = "Given an over-length branch, when validated, then it fails")]
    public void RefuseOverLongBranch()
    {
        var command = new EnqueueMergeRequestCommand(
            ProjectId.New(),
            new string('a', MergeQueueValidator.MaxBranchNameLength + 1),
            "https://example.com/pr/1",
            ConflictResolution.None,
            null);

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.PropertyName == "BranchName");
    }
}

/// <summary>
/// Structural validation of <see cref="ClaimMergeQueueCommand"/>:
/// non-empty operator id, bounded length.
/// </summary>
public sealed class ClaimMergeQueueValidatorShould
{
    private readonly ClaimMergeQueueValidator validator = new();

    [Fact(DisplayName = "Given a Claim without operator id, when validated, then it fails")]
    public void RequireOperatorIdOnClaim()
    {
        var command = new ClaimMergeQueueCommand(Guid.CreateVersion7(), OperatorId: " ");

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.PropertyName == "OperatorId");
    }

    [Fact(DisplayName = "Given an empty entry id, when validated, then it fails")]
    public void RefuseEmptyEntryId()
    {
        var command = new ClaimMergeQueueCommand(Guid.Empty, "operator-alice");

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.PropertyName == "EntryId");
    }
}

/// <summary>
/// Structural validation of <see cref="AbandonMergeQueueCommand"/>:
/// non-empty reason, bounded length.
/// </summary>
public sealed class AbandonMergeQueueValidatorShould
{
    private readonly AbandonMergeQueueValidator validator = new();

    [Fact(DisplayName = "Given an Abandon without reason, when validated, then it fails")]
    public void RequireReasonOnAbandon()
    {
        var command = new AbandonMergeQueueCommand(Guid.CreateVersion7(), Reason: " ");

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.PropertyName == "Reason");
    }
}

/// <summary>
/// Structural validation of <see cref="AnnotateMergeQueueCommand"/>:
/// bounded notes; null clears notes.
/// </summary>
public sealed class AnnotateMergeQueueValidatorShould
{
    private readonly AnnotateMergeQueueValidator validator = new();

    [Fact(DisplayName = "Given an annotate with null notes, when validated, then it passes")]
    public void AcceptNullNotes()
    {
        var command = new AnnotateMergeQueueCommand(Guid.CreateVersion7(), Notes: null);

        var result = validator.Validate(command);

        result.IsValid.ShouldBeTrue();
    }

    [Fact(DisplayName = "Given an annotate with over-length notes, when validated, then it fails")]
    public void RefuseOverLengthNotes()
    {
        var command = new AnnotateMergeQueueCommand(Guid.CreateVersion7(), new string('n', AnnotateMergeQueueValidator.MaxNotesLength + 1));

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.PropertyName == "Notes");
    }
}

/// <summary>
/// Release and Merge handlers share a "non-empty id" rule — covered in
/// the <see cref="ClaimMergeQueueValidatorShould.RefuseEmptyEntryId"/>
/// above; the per-verb validators inherit the same rule.
/// </summary>
public sealed class ReleaseAndMergeMergeQueueValidatorShould
{
    [Fact(DisplayName = "Given a release with an empty entry id, when validated, then it fails")]
    public void RefuseEmptyEntryIdOnRelease()
    {
        var result = new ReleaseMergeQueueValidator().Validate(new ReleaseMergeQueueCommand(Guid.Empty));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.PropertyName == "EntryId");
    }

    [Fact(DisplayName = "Given a merge with an empty entry id, when validated, then it fails")]
    public void RefuseEmptyEntryIdOnMerge()
    {
        var result = new MergeMergeQueueValidator().Validate(new MergeMergeQueueCommand(Guid.Empty));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.PropertyName == "EntryId");
    }
}
