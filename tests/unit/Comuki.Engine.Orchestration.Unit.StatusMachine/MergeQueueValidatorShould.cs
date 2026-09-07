using Comuki.Engine.Orchestration.Application.MergeQueue;
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
/// Structural validation of <see cref="UpdateMergeQueueCommand"/>: operator id on
/// Claim / Abandon, reason on Abandon, bounded string lengths.
/// </summary>
public sealed class UpdateMergeQueueValidatorShould
{
    private readonly UpdateMergeQueueValidator validator = new();

    [Fact(DisplayName = "Given a Claim without operator id, when validated, then it fails")]
    public void RequireOperatorIdOnClaim()
    {
        var command = new UpdateMergeQueueCommand(
            Guid.CreateVersion7(),
            MergeQueueAction.Claim,
            OperatorId: null,
            Reason: null,
            Notes: null);

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.PropertyName == "OperatorId");
    }

    [Fact(DisplayName = "Given an Abandon without reason, when validated, then it fails")]
    public void RequireReasonOnAbandon()
    {
        var command = new UpdateMergeQueueCommand(
            Guid.CreateVersion7(),
            MergeQueueAction.Abandon,
            OperatorId: "operator-alice",
            Reason: " ",
            Notes: null);

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.PropertyName == "Reason");
    }

    [Fact(DisplayName = "Given an empty entry id, when validated, then it fails")]
    public void RefuseEmptyEntryId()
    {
        var command = new UpdateMergeQueueCommand(
            Guid.Empty,
            MergeQueueAction.Merge,
            OperatorId: null,
            Reason: null,
            Notes: null);

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.PropertyName == "EntryId");
    }

    [Fact(DisplayName = "Given a Merge command, when validated, then it passes without operator id")]
    public void AcceptMergeWithoutOperator()
    {
        var command = new UpdateMergeQueueCommand(
            Guid.CreateVersion7(),
            MergeQueueAction.Merge,
            OperatorId: null,
            Reason: null,
            Notes: null);

        var result = validator.Validate(command);

        result.IsValid.ShouldBeTrue();
    }
}
