using Comuki.Modules.Identity.Application.Authorization;
using Comuki.Modules.Identity.Application.Ports;
using Comuki.Modules.Identity.Application.Views;
using Comuki.Modules.Identity.Domain.Assignments;
using Comuki.Modules.Identity.Domain.Roles;

namespace Comuki.Modules.Identity.Application.Assignments.Grant;

/// <summary>
/// Grants one role assignment: escalation guard (no role above the
/// grantor's own seniority), duplicate guard (one active assignment per
/// subject+role+scope — the partial unique index backs it), then write
/// and cache invalidation so the new permissions are visible immediately.
/// </summary>
/// <param name="assignments"></param>
/// <param name="evaluator"></param>
/// <param name="clock"></param>
public sealed class GrantRoleHandler(
    IRoleAssignmentStore assignments,
    IPermissionEvaluator evaluator,
    TimeProvider clock)
{
    /// <summary>Grants the assignment.</summary>
    /// <param name="command"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException">Escalation, or an active assignment already exists.</exception>
    public async Task<RoleAssignmentView> HandleAsync(GrantRoleCommand command, CancellationToken cancellationToken = default)
    {
        if (command.ActingAs is { } actingAs)
        {
            var granterAssignments = await assignments.ListActiveAsync(actingAs, cancellationToken);
            var granterSeniority = granterAssignments
                .Select(static assignment => RoleMatrix.SeniorityOf(assignment.Role))
                .DefaultIfEmpty(0)
                .Max();

            RoleSeniorityGuard.EnsureGrantable(command.Role, granterSeniority);
        }

        if (await assignments.FindActiveAsync(command.Grantee, command.Role, command.Scope, cancellationToken) is not null)
        {
            throw new InvalidOperationException(
                $"an active assignment of '{RoleKeys.Key(command.Role)}' for {command.Grantee} at this scope already exists");
        }

        var assignment = RoleAssignment.Create(command.Grantee, command.Role, command.Scope, command.ActingAs, clock.GetUtcNow());

        await assignments.SaveAsync(assignment, cancellationToken);
        evaluator.Invalidate(command.Grantee);

        return AccountMapper.ToView(assignment);
    }
}
