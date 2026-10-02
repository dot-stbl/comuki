using Comuki.Modules.Projects.Application.Projects.Create;
using Comuki.Modules.Projects.Domain.Projects;
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

    [Fact(DisplayName = "Given well-formed identity fields, when validated, then the command passes")]
    public void AcceptWellFormedIdentity()
    {
        var command = new CreateProjectCommand("Web Platform", "web-platform", null, null, null,
            Icon: "🛰️", Color: "#3C5A86", Tags: ["Web", "billing"]);

        var result = validator.Validate(command);

        result.IsValid.ShouldBeTrue();
    }

    [Theory(DisplayName = "Given a malformed colour, when validated, then it fails on Color")]
    [InlineData("3c5a86")]
    [InlineData("#3C5A8")]
    [InlineData("#3c5a86x")]
    [InlineData("blue")]
    public void RefuseMalformedColor(string color)
    {
        var command = new CreateProjectCommand("Web Platform", "web-platform", null, null, null, Color: color);

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.PropertyName == "Color");
    }

    [Theory(DisplayName = "Given a malformed tag, when validated, then it fails on Tags")]
    [InlineData("Data_Pipeline")]
    [InlineData("-infra")]
    [InlineData("spaced tag")]
    public void RefuseMalformedTag(string tag)
    {
        var command = new CreateProjectCommand("Web Platform", "web-platform", null, null, null, Tags: [tag]);

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.PropertyName == "Tags");
    }

    [Fact(DisplayName = "Given 21 distinct tags, when validated, then it fails on Tags")]
    public void RefuseOversizedTagList()
    {
        var tags = Enumerable.Range(1, 21).Select(static index => $"tag-{index}").ToArray();
        var command = new CreateProjectCommand("Web Platform", "web-platform", null, null, null, Tags: tags);

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.PropertyName == "Tags");
    }

    [Fact(DisplayName = "Given 21 entries that normalize to 20 distinct tags, when validated, then the command passes")]
    public void AcceptTagListWithinBudgetAfterNormalization()
    {
        var tags = Enumerable.Range(1, 20).Select(static index => $"tag-{index}").ToList();
        tags.Add(" Tag-1 ");
        var command = new CreateProjectCommand("Web Platform", "web-platform", null, null, null, Tags: tags);

        var result = validator.Validate(command);

        result.IsValid.ShouldBeTrue();
    }

    [Fact(DisplayName = "Given an icon over 200 chars, when validated, then it fails on Icon")]
    public void RefuseOversizedIcon()
    {
        var command = new CreateProjectCommand("Web Platform", "web-platform", null, null, null,
            Icon: new string('x', Project.MaxIconLength + 1));

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.PropertyName == "Icon");
    }

    [Fact(DisplayName = "Given an explicitly empty icon, when validated, then it fails on Icon")]
    public void RefuseEmptyIcon()
    {
        var command = new CreateProjectCommand("Web Platform", "web-platform", null, null, null, Icon: "");

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.PropertyName == "Icon");
    }

    [Fact(DisplayName = "Given a catalog-shaped env class, when validated, then the command passes")]
    public void AcceptWellFormedEnvClass()
    {
        var command = new CreateProjectCommand("Web Platform", "web-platform", null, null, null,
            EnvClass: "net10-sdk-bun");

        var result = validator.Validate(command);

        result.IsValid.ShouldBeTrue();
    }

    [Theory(DisplayName = "Given a malformed env class, when validated, then it fails on EnvClass")]
    [InlineData("Bad_Class")]
    [InlineData("net10 sdk")]
    [InlineData("-net10")]
    public void RefuseMalformedEnvClass(string envClass)
    {
        var command = new CreateProjectCommand("Web Platform", "web-platform", null, null, null,
            EnvClass: envClass);

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.PropertyName == "EnvClass");
    }

    [Fact(DisplayName = "Given an env class over the column bound, when validated, then it fails on EnvClass")]
    public void RefuseOversizedEnvClass()
    {
        var command = new CreateProjectCommand("Web Platform", "web-platform", null, null, null,
            EnvClass: new string('a', Project.MaxEnvClassLength + 1));

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.PropertyName == "EnvClass");
    }
}
