using Comuki.Engine.Orchestration.Application.MergeQueue;
using Comuki.Engine.Orchestration.Application.MergeQueue.BatchAbandon;
using Comuki.Engine.Orchestration.Application.MergeQueue.BatchClaim;
using Comuki.Engine.Orchestration.Application.MergeQueue.BatchMerge;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Orchestration.Unit.StatusMachine;

/// <summary>
/// Structural validation of <see cref="CreateMergeBatchCommand"/>:
/// non-empty name, non-empty URL list, bounded URL length per entry.
/// </summary>
public sealed class MergeBatchValidatorShould
{
    private readonly MergeBatchValidator validator = new();

    [Fact(DisplayName = "Given a well-formed command, when validated, then it passes")]
    public void AcceptWellFormedCommand()
    {
        var command = new CreateMergeBatchCommand(
            "release-train-q3",
            ["https://example.com/pr/1", "https://example.com/pr/2"]);

        var result = validator.Validate(command);

        result.IsValid.ShouldBeTrue();
    }

    [Theory(DisplayName = "Given an empty name, when validated, then it fails")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void RefuseEmptyName(string? name)
    {
        var command = new CreateMergeBatchCommand(
            name!,
            ["https://example.com/pr/1"]);

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.PropertyName == "Name");
    }

    [Fact(DisplayName = "Given an empty URL list, when validated, then it fails")]
    public void RefuseEmptyUrlList()
    {
        var command = new CreateMergeBatchCommand(
            "release-train-q3",
            []);

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.PropertyName == "PullRequestUrls");
    }

    [Fact(DisplayName = "Given an over-length URL, when validated, then it fails")]
    public void RefuseOverLongUrl()
    {
        var command = new CreateMergeBatchCommand(
            "release-train-q3",
            [new string('u', MergeBatchValidator.MaxPullRequestUrlLength + 1)]);

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given an over-cap URL list, when validated, then it fails")]
    public void RefuseOverCapUrlList()
    {
        var urls = new string[MergeBatchValidator.MaxUrlsPerBatch + 1];
        for (var index = 0; index < urls.Length; index++)
        {
            urls[index] = $"https://example.com/pr/{index}";
        }

        var command = new CreateMergeBatchCommand("release-train-q3", urls);

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
    }
}

/// <summary>
/// Structural validation of <see cref="AbandonMergeBatchCommand"/>:
/// non-empty reason, bounded length.
/// </summary>
public sealed class AbandonMergeBatchValidatorShould
{
    private readonly AbandonMergeBatchValidator validator = new();

    [Fact(DisplayName = "Given an Abandon without reason, when validated, then it fails")]
    public void RequireReasonOnAbandon()
    {
        var command = new AbandonMergeBatchCommand(Guid.CreateVersion7(), Reason: " ");

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.PropertyName == "Reason");
    }

    [Fact(DisplayName = "Given an empty batch id, when validated, then it fails")]
    public void RefuseEmptyBatchId()
    {
        var command = new AbandonMergeBatchCommand(Guid.Empty, Reason: "stale batch");

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.PropertyName == "BatchId");
    }
}

/// <summary>
/// Claim and Merge handlers share a "non-empty batch id" rule.
/// </summary>
public sealed class ClaimAndMergeMergeBatchValidatorShould
{
    [Fact(DisplayName = "Given a claim with an empty batch id, when validated, then it fails")]
    public void RefuseEmptyBatchIdOnClaim()
    {
        var result = new ClaimMergeBatchValidator().Validate(new ClaimMergeBatchCommand(Guid.Empty));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.PropertyName == "BatchId");
    }

    [Fact(DisplayName = "Given a merge with an empty batch id, when validated, then it fails")]
    public void RefuseEmptyBatchIdOnMerge()
    {
        var result = new MergeMergeBatchValidator().Validate(new MergeMergeBatchCommand(Guid.Empty));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.PropertyName == "BatchId");
    }
}
