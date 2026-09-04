using FluentValidation;

namespace Comuki.Host.Auth.Models;

/// <summary>Toggle the disabled flag (PATCH /api/v1/users/{userId}).</summary>
public sealed class SetUserDisabledRequest
{
    /// <summary>New disabled flag value.</summary>
    public required bool Disabled { get; init; }
}

/// <summary>Validation of <see cref="SetUserDisabledRequest"/> — placeholder; the request shape is trivial.</summary>
public sealed class SetUserDisabledRequestValidator : AbstractValidator<SetUserDisabledRequest>
{
    /// <summary>No rules — every <c>bool</c> is acceptable; kept for symmetry with the rest of the surface.</summary>
    public SetUserDisabledRequestValidator()
    {
    }
}
