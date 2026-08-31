using Comuki.Engine.Orchestration.Application.Models;
using Comuki.Engine.Orchestration.Application.Validation;
using Comuki.Shared.Contracts.Queue;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Orchestration.Unit.StatusMachine;

/// <summary>Structural rules of <see cref="ClaimWorkItemValidator"/>.</summary>
public sealed class ClaimWorkItemValidatorShould
{
    [Theory(DisplayName = "Given invalid claim commands, when validated, then they fail")]
    [InlineData("", "refs/heads/main", "implement", "Image")]
    [InlineData("worker:dev", "", "implement", "ProfilesRef")]
    [InlineData("worker:dev", "refs/heads/main", "", "ProfileKey")]
    [InlineData("worker:dev", "refs/heads/main", "Implement_1", "ProfileKey")]
    public void RejectInvalidLabels(string image, string profilesRef, string profileKey, string expectedInvalidProperty)
    {
        var validator = new ClaimWorkItemValidator();
        var command = new ClaimWorkItemCommand(WorkerId.New(), new WorkItemLabels(image, profilesRef, profileKey));

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(error => error.PropertyName == $"Labels.{expectedInvalidProperty}");
    }

    [Fact(DisplayName = "Given an empty worker id, when validated, then it fails")]
    public void RejectEmptyWorkerId()
    {
        var validator = new ClaimWorkItemValidator();
        var command = new ClaimWorkItemCommand(
            new WorkerId(Guid.Empty), new WorkItemLabels("worker:dev", "refs/heads/main", "implement"));

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static error => error.PropertyName == "WorkerId.Value");
    }

    [Fact(DisplayName = "Given a well-formed claim command, when validated, then it passes")]
    public void AcceptValidCommand()
    {
        var validator = new ClaimWorkItemValidator();
        var command = new ClaimWorkItemCommand(
            WorkerId.New(), new WorkItemLabels("ghcr.io/comuki/worker@sha256:9f86d0", "refs/heads/main", "explore-readonly"));

        var result = validator.Validate(command);

        result.IsValid.ShouldBeTrue();
    }
}
