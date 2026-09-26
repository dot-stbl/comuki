using Comuki.Modules.Projects.Domain.Attachments;
using FluentValidation;

namespace Comuki.Modules.Projects.Application.Attachments;

/// <summary>
/// Structural validation of <see cref="AttachRepositoryCommand"/> — the role
/// pattern, the declared access and the credential override length. Semantic
/// checks (project exists, attachment pair unique) live in the handler,
/// backed by the unique index as the last-line of defence.
/// </summary>
public sealed class AttachRepositoryValidator : AbstractValidator<AttachRepositoryCommand>
{
    /// <summary>Role shape: trim + lower-case kebab-case, no spaces; mirrors the normalized stored form.</summary>
    public const string RolePattern = "^[a-z0-9]+(-[a-z0-9]+)*$";

    /// <summary>Rules: role shape/length, access not Unspecified, optional override length bound.</summary>
    public AttachRepositoryValidator()
    {
        RuleFor(static command => command.Role)
            .NotEmpty()
            .MaximumLength(AttachmentRole.MaxLength)
            .Matches(RolePattern)
            .WithMessage("role must be lower-case kebab-case (a-z, 0-9, single dashes)");

        RuleFor(static command => command.Access)
            .NotEqual(AttachmentAccess.Unspecified)
            .WithMessage("access must not be Unspecified");

        RuleFor(static command => command.CredentialOverrideRef)
            .MaximumLength(ProjectRepositoryAttachment.CredentialOverrideRefMaxLength);
    }
}
