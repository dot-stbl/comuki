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

    [Fact(DisplayName = "Given a well-formed identity patch, when validated, then it passes")]
    public void AcceptWellFormedIdentityPatch()
    {
        var command = new UpdateProjectCommand(ProjectId.New(), null, null, null, null,
            Icon: "🛰️", Color: "#3C5A86", Tags: ["Web", "billing"]);

        var result = validator.Validate(command);

        result.IsValid.ShouldBeTrue();
    }

    [Fact(DisplayName = "Given a malformed colour on a patch, when validated, then it fails on Color")]
    public void RefuseMalformedColorWhenProvided()
    {
        var command = new UpdateProjectCommand(ProjectId.New(), null, null, null, null, Color: "3c5a86");

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.PropertyName == "Color");
    }

    [Fact(DisplayName = "Given a malformed tag on a patch, when validated, then it fails on Tags")]
    public void RefuseMalformedTagWhenProvided()
    {
        var command = new UpdateProjectCommand(ProjectId.New(), null, null, null, null, Tags: ["Data_Pipeline"]);

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.PropertyName == "Tags");
    }

    [Fact(DisplayName = "Given 21 distinct tags on a patch, when validated, then it fails on Tags")]
    public void RefuseOversizedTagList()
    {
        var tags = Enumerable.Range(1, 21).Select(static index => $"tag-{index}").ToArray();
        var command = new UpdateProjectCommand(ProjectId.New(), null, null, null, null, Tags: tags);

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.PropertyName == "Tags");
    }

    [Fact(DisplayName = "Given an explicitly empty icon on a patch, when validated, then it fails on Icon")]
    public void RefuseEmptyIconWhenProvided()
    {
        var command = new UpdateProjectCommand(ProjectId.New(), null, null, null, null, Icon: "");

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.PropertyName == "Icon");
    }

    [Fact(DisplayName = "Given an empty tags list on a patch, when validated, then it passes (clearing, not malformed)")]
    public void AcceptEmptyTagsList()
    {
        var command = new UpdateProjectCommand(ProjectId.New(), null, null, null, null, Tags: []);

        var result = validator.Validate(command);

        result.IsValid.ShouldBeTrue();
    }
}
