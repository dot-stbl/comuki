using FluentValidation;

namespace Comuki.Modules.Identity.Application.Sessions;

/// <summary>Structural validation of <see cref="LoginCommand"/> — credential checking is the handler's job.</summary>
public sealed class LoginValidator : AbstractValidator<LoginCommand>
{
    /// <summary>Rules: both fields present and bounded.</summary>
    public LoginValidator()
    {
        _ = RuleFor(static command => command.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(320);

        _ = RuleFor(static command => command.Password)
            .NotEmpty()
            .MaximumLength(128);
    }
}
