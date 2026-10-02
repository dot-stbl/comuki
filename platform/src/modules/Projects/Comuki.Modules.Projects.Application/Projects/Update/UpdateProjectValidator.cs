using Comuki.Modules.Projects.Domain.Projects;
using FluentValidation;

namespace Comuki.Modules.Projects.Application.Projects.Update;

/// <summary>Structural validation of <see cref="UpdateProjectCommand"/> — absent fields skip their rules.</summary>
public sealed class UpdateProjectValidator : AbstractValidator<UpdateProjectCommand>
{
    /// <summary>Rules: optional name must be non-empty when provided; length bounds on every optional field; identity-field shape.</summary>
    public UpdateProjectValidator()
    {
        RuleFor(static command => command.Name)
            .NotEmpty()
            .When(static command => command.Name is not null)
            .MaximumLength(128);

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

        // PATCH shape: null leaves the stored class untouched, an empty
        // string clears the binding (operator's way to take a project
        // back to "no class"), a non-empty value must match the catalog
        // id pattern and fit the column bound.
        RuleFor(static command => command.EnvClass)
            .MaximumLength(Project.MaxEnvClassLength)
            .Must(static envClass => envClass is null
                || envClass.Length == 0
                || ProjectIdentityRules.EnvClassIsWellFormed(envClass))
            .WithMessage($"envClass, when provided, must match '{Project.EnvClassPattern}' or be empty to clear");
    }
}
