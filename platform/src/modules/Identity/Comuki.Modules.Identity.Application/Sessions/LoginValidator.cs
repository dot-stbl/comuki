using FluentValidation;

namespace Comuki.Modules.Identity.Application.Sessions;

/// <summary>Structural validation of <see cref="LoginCommand"/> — credential checking is the handler's job.</summary>
public sealed class LoginValidator : AbstractValidator<LoginCommand>
{
    /// <summary>Rules: both fields present and bounded.</summary>
    public LoginValidator()
    {
        RuleFor(static command => command.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(320);

        RuleFor(static command => command.Password)
            .NotEmpty()
            .MaximumLength(128);
    }
}
