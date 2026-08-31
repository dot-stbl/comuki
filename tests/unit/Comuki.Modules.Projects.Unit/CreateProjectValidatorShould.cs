using Comuki.Modules.Projects.Application.Projects.Create;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Projects.Unit;

/// <summary>
/// Structural validation of <see cref="CreateProjectCommand"/>: the slug
/// is the URL key of the project — the pattern is strict (lower-case
/// kebab-case) so every accepted slug is stable forever.
/// </summary>
public sealed class CreateProjectValidatorShould
{
    private readonly CreateProjectValidator validator = new();

    [Fact(DisplayName = "Given a well-formed command, when validated, then it passes")]
    public void AcceptWellFormedCommand()
    {
        var command = new CreateProjectCommand(
            "Web Platform",
            "web-platform",
            "customer portal",
            "https://git.example.com/acme/profiles.git",
            "refs/tags/v1");

        var result = validator.Validate(command);

        result.IsValid.ShouldBeTrue();
    }

    [Fact(DisplayName = "Given an empty name, when validated, then it fails")]
    public void RefuseEmptyName()
    {
        var command = new CreateProjectCommand("", "web-platform", null, null, null);

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.PropertyName == "Name");
    }

    [Theory(DisplayName = "Given a malformed slug, when validated, then it fails")]
    [InlineData("Web")]
    [InlineData("web platform")]
    [InlineData("-web")]
    [InlineData("web-")]
    [InlineData("web--platform")]
    [InlineData("ab")]
    [InlineData("web_platform")]
    public void RefuseMalformedSlug(string slug)
    {
        var command = new CreateProjectCommand("Web Platform", slug, null, null, null);

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.PropertyName == "Slug");
    }

    [Fact(DisplayName = "Given a description over 2000 chars, when validated, then it fails")]
    public void RefuseOverlongDescription()
    {
        var command = new CreateProjectCommand("Web Platform", "web-platform", new string('x', 2001), null, null);

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.PropertyName == "Description");
    }
}
