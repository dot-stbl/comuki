using FluentValidation;

namespace Comuki.Host.Auth.Models;

/// <summary>Invite user body (POST /api/v1/users).</summary>
public sealed class InviteUserRequest
{
    /// <summary>Login email (unique).</summary>
    public required string Email { get; init; } = string.Empty;

    /// <summary>Optional display name; defaults to the email local-part when omitted.</summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Optional bootstrap password. When omitted the operator intends to send a
    /// separate invitation link and the new account lands password-less.
    /// </summary>
    public string? Password { get; init; }
}

/// <summary>Validation of <see cref="InviteUserRequest"/> — request-level only; semantic rules live in the handler.</summary>
public sealed class InviteUserRequestValidator : AbstractValidator<InviteUserRequest>
{
    /// <summary>Rules: email shape/length, optional display-name bounds, optional password bounds.</summary>
    public InviteUserRequestValidator()
    {
        RuleFor(static request => request.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(320);

        RuleFor(static request => request.DisplayName)
            .MaximumLength(256)
            .When(static request => request.DisplayName is not null);

        RuleFor(static request => request.Password)
            .MinimumLength(8)
            .MaximumLength(128)
            .When(static request => request.Password is not null);
    }
}
