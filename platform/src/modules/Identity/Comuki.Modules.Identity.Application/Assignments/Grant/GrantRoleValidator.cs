using Comuki.Modules.Identity.Domain.Scopes;
using FluentValidation;

namespace Comuki.Modules.Identity.Application.Assignments.Grant;

/// <summary>Structural validation of <see cref="GrantRoleCommand"/> — the escalation rule is the handler's domain guard.</summary>
public sealed class GrantRoleValidator : AbstractValidator<GrantRoleCommand>
{
    /// <summary>Rules: non-empty subject id, coherent scope (project scope needs a project id).</summary>
    public GrantRoleValidator()
    {
        _ = RuleFor(static command => command.Grantee.Id)
            .Must(static id => id != Guid.Empty);

        _ = RuleFor(static command => command.Scope)
            .Must(static scope => scope.Level != ScopeLevel.Project
                || (scope.ProjectId is { } project && project.Value != Guid.Empty))
            .WithMessage("project scope requires a project id");
    }
}
