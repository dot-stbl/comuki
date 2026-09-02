using FluentValidation;

namespace Comuki.Modules.Intake.Application.Tickets;

/// <summary>Structural validation of a native ticket command.</summary>
public sealed class CreateNativeTicketValidator : AbstractValidator<CreateNativeTicketCommand>
{
    /// <summary>Rules for the native ticket shape.</summary>
    public CreateNativeTicketValidator()
    {
        RuleFor(static command => command.Title)
            .NotEmpty()
            .MaximumLength(512);

        RuleFor(static command => command.Body)
            .MaximumLength(32768);

        RuleFor(static command => command.ExternalId)
            .MaximumLength(512)
            .Matches("^[\\w./#:-]+$")
            .When(static command => command.ExternalId is { Length: > 0 }, ApplyConditionTo.CurrentValidator)
            .WithMessage("external id may contain only letters, digits and . / # : _ -");

        RuleFor(static command => command.Author)
            .MaximumLength(256);
    }
}
