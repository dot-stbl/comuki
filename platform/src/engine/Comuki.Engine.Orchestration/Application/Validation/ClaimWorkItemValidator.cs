using Comuki.Engine.Orchestration.Application.Models;
using FluentValidation;

namespace Comuki.Engine.Orchestration.Application.Validation;

/// <summary>
/// Structural validation of <see cref="ClaimWorkItemCommand"/>: non-empty
/// worker, and labels the queue can actually match on (bounded, slug-shaped
/// profile key).
/// </summary>
public sealed class ClaimWorkItemValidator : AbstractValidator<ClaimWorkItemCommand>
{
    /// <summary>Configures the claim rules.</summary>
    public ClaimWorkItemValidator()
    {
        RuleFor(static command => command.WorkerId.Value)
            .NotEqual(Guid.Empty)
            .WithMessage("worker id must not be empty");

        RuleFor(static command => command.Labels.Image)
            .NotEmpty()
            .MaximumLength(512);

        RuleFor(static command => command.Labels.ProfilesRef)
            .NotEmpty()
            .MaximumLength(256);

        RuleFor(static command => command.Labels.ProfileKey)
            .NotEmpty()
            .MaximumLength(128)
            .Matches("^[a-z0-9][a-z0-9-]*$")
            .WithMessage("profile key must be a lowercase slug (letters, digits, dashes)");
    }
}
