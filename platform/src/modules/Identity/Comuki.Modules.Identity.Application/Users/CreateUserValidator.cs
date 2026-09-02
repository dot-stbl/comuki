using FluentValidation;

namespace Comuki.Modules.Identity.Application.Users;

/// <summary>Structural validation of <see cref="CreateUserCommand"/> — semantic rules live in the handler.</summary>
public sealed class CreateUserValidator : AbstractValidator<CreateUserCommand>
{
    /// <summary>Rules: email shape/length, non-empty display name, password length bounds.</summary>
    public CreateUserValidator()
    {
        RuleFor(static command => command.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(320);

        RuleFor(static command => command.DisplayName)
            .NotEmpty()
            .MaximumLength(256);

        RuleFor(static command => command.Password)
            .NotEmpty()
            .MinimumLength(8)
            .MaximumLength(128);
    }
}
