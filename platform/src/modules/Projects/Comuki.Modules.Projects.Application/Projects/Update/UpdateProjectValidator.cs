using FluentValidation;

namespace Comuki.Modules.Projects.Application.Projects.Update;

/// <summary>Structural validation of <see cref="UpdateProjectCommand"/> — absent fields skip their rules.</summary>
public sealed class UpdateProjectValidator : AbstractValidator<UpdateProjectCommand>
{
    /// <summary>Rules: optional name must be non-empty when provided; length bounds on every optional field.</summary>
    public UpdateProjectValidator()
    {
        _ = RuleFor(static command => command.Name)
            .NotEmpty()
            .When(static command => command.Name is not null)
            .MaximumLength(128);

        _ = RuleFor(static command => command.Description)
            .MaximumLength(2000);

        _ = RuleFor(static command => command.ProfilesGitUrl)
            .MaximumLength(2048);

        _ = RuleFor(static command => command.ProfilesGitRef)
            .MaximumLength(256);
    }
}
