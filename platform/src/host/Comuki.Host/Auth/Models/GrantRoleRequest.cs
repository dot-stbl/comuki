using Comuki.Modules.Identity.Domain.Roles;
using FluentValidation;

namespace Comuki.Host.Auth.Models;

/// <summary>Grant role body (POST /api/v1/grants).</summary>
public sealed class GrantRoleRequest
{
    /// <summary>Target user id.</summary>
    public required Guid UserId { get; init; }

    /// <summary>Role key (kebab-case: <c>platform-admin</c>, <c>member</c>, …).</summary>
    public required string Role { get; init; } = string.Empty;

    /// <summary>Optional project id — when present the grant is project-scoped, otherwise platform-scoped.</summary>
    public Guid? ProjectId { get; init; }
}

/// <summary>Validation of <see cref="GrantRoleRequest"/> — required fields plus the role key vocabulary.</summary>
public sealed class GrantRoleRequestValidator : AbstractValidator<GrantRoleRequest>
{
    /// <summary>Rules: user id, role key known, project id optional but non-empty when present.</summary>
    public GrantRoleRequestValidator()
    {
        RuleFor(static request => request.UserId)
            .NotEqual(Guid.Empty);

        RuleFor(static request => request.Role)
            .NotEmpty()
            .MaximumLength(64)
            .Must(static role => RoleKeys.Parse(role) is not null)
            .WithMessage("role must be one of: platform-admin, operator, project-admin, approver, member, viewer");
    }
}
