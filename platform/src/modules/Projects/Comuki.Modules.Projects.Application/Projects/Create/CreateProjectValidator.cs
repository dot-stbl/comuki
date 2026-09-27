using Comuki.Modules.Projects.Domain.Projects;
using FluentValidation;

namespace Comuki.Modules.Projects.Application.Projects.Create;

/// <summary>
/// Structural validation of <see cref="CreateProjectCommand"/> — semantic
/// rules (slug uniqueness) live in the handler, backed by the unique index.
/// </summary>
public sealed class CreateProjectValidator : AbstractValidator<CreateProjectCommand>
{
    /// <summary>Slug shape: lower-case kebab-case, 3–64 chars (the URL key of the project).</summary>
    public const string SlugPattern = "^[a-z0-9]+(-[a-z0-9]+)*$";

    /// <summary>Rules: name shape/length, slug pattern, optional field length bounds, identity-field shape.</summary>
    public CreateProjectValidator()
    {
        RuleFor(static command => command.Name)
            .NotEmpty()
            .MaximumLength(128);

        RuleFor(static command => command.Slug)
            .NotEmpty()
            .Matches(SlugPattern)
            .WithMessage("slug must be lower-case kebab-case (a-z, 0-9, single dashes)")
            .Length(3, 64);

        RuleFor(static command => command.Description)
            .MaximumLength(2000);

        RuleFor(static command => command.ProfilesGitUrl)
            .MaximumLength(2048);

        RuleFor(static command => command.ProfilesGitRef)
            .MaximumLength(256);

        RuleFor(static command => command.Icon)
            .NotEmpty()
            .When(static command => command.Icon is not null)
            .MaximumLength(Project.MaxIconLength);

        RuleFor(static command => command.Color)
            .NotEmpty()
            .When(static command => command.Color is not null)
            .Must(static color => color is null || ProjectIdentityRules.ColorIsWellFormed(color))
            .WithMessage("color must be a #rrggbb hex value, e.g. #3c5a86");

        RuleFor(static command => command.Tags)
            .Must(static tags => tags is null || ProjectIdentityRules.TagsAreWellFormed(tags))
            .WithMessage(
                $"tags must match '{Project.TagPattern}' after trimming and number at most {Project.MaxTags} distinct");
    }
}
