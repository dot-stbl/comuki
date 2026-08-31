using Comuki.Modules.Projects.Application.Projects.Update;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Projects.Unit;

/// <summary>
/// Structural validation of <see cref="UpdateProjectCommand"/> (PATCH
/// semantics): absent fields skip their rules, provided fields must be
/// well-formed.
/// </summary>
public sealed class UpdateProjectValidatorShould
{
    private readonly UpdateProjectValidator validator = new();

    [Fact(DisplayName = "Given a command with only nulls, when validated, then it passes")]
    public void AcceptAllNullPatch()
    {
        var command = new UpdateProjectCommand(ProjectId.New(), null, null, null, null);

        var result = validator.Validate(command);

        result.IsValid.ShouldBeTrue();
    }

    [Fact(DisplayName = "Given an explicitly empty name, when validated, then it fails")]
    public void RefuseEmptyNameWhenProvided()
    {
        var command = new UpdateProjectCommand(ProjectId.New(), "", null, null, null);

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.PropertyName == "Name");
    }

    [Fact(DisplayName = "Given a git url over 2048 chars, when validated, then it fails")]
    public void RefuseOverlongGitUrl()
    {
        var command = new UpdateProjectCommand(ProjectId.New(), null, null, new string('x', 2049), null);

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.PropertyName == "ProfilesGitUrl");
    }
}
