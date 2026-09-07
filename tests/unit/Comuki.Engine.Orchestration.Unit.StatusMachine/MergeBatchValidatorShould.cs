using Comuki.Engine.Orchestration.Application.MergeQueue;
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
/// Structural validation of <see cref="UpdateMergeBatchCommand"/>:
/// reason on Abandon, bounded string length.
/// </summary>
public sealed class UpdateMergeBatchValidatorShould
{
    private readonly UpdateMergeBatchValidator validator = new();

    [Fact(DisplayName = "Given an Abandon without reason, when validated, then it fails")]
    public void RequireReasonOnAbandon()
    {
        var command = new UpdateMergeBatchCommand(
            Guid.CreateVersion7(),
            MergeBatchAction.Abandon,
            Reason: " ");

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.PropertyName == "Reason");
    }

    [Fact(DisplayName = "Given an empty batch id, when validated, then it fails")]
    public void RefuseEmptyBatchId()
    {
        var command = new UpdateMergeBatchCommand(
            Guid.Empty,
            MergeBatchAction.Merge,
            Reason: null);

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.PropertyName == "BatchId");
    }

    [Fact(DisplayName = "Given a Merge command, when validated, then it passes without reason")]
    public void AcceptMergeWithoutReason()
    {
        var command = new UpdateMergeBatchCommand(
            Guid.CreateVersion7(),
            MergeBatchAction.Merge,
            Reason: null);

        var result = validator.Validate(command);

        result.IsValid.ShouldBeTrue();
    }
}
