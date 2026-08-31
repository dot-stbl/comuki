using Comuki.Modules.Identity.Application.Authorization;
using Comuki.Modules.Identity.Application.Ports;
using Comuki.Modules.Identity.Application.Views;
using Comuki.Modules.Identity.Domain.Assignments;
using Comuki.Modules.Identity.Domain.Roles;

namespace Comuki.Modules.Identity.Application.Assignments.Revoke;

/// <summary>
/// Revokes an assignment: the same seniority rule as granting (the sets
/// of who can grant and who can revoke must coincide), then write and
/// cache invalidation.
/// </summary>
/// <param name="assignments"></param>
/// <param name="evaluator"></param>
/// <param name="clock"></param>
public sealed class RevokeRoleHandler(
    IRoleAssignmentStore assignments,
    IPermissionEvaluator evaluator,
    TimeProvider clock)
{
    /// <summary>Revokes the assignment.</summary>
    /// <param name="command"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException">No such active assignment, or the revoker is too junior.</exception>
    public async Task<RoleAssignmentView> HandleAsync(RevokeRoleCommand command, CancellationToken cancellationToken = default)
    {
        var assignment = await assignments.FindActiveAsync(command.AssignmentId, cancellationToken)
            ?? throw new InvalidOperationException($"no active assignment {command.AssignmentId}");

        if (command.ActingAs is { } actingAs)
        {
            var revokerAssignments = await assignments.ListActiveAsync(actingAs, cancellationToken);
            var revokerSeniority = revokerAssignments
                .Select(static row => RoleMatrix.SeniorityOf(row.Role))
                .DefaultIfEmpty(0)
                .Max();

            RoleSeniorityGuard.EnsureGrantable(assignment.Role, revokerSeniority);
        }

        var subject = assignment.Subject;
        assignment.Revoke(clock.GetUtcNow());

        await assignments.SaveAsync(assignment, cancellationToken);
        evaluator.Invalidate(subject);

        return AccountMapper.ToView(assignment);
    }
}
