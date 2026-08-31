using Comuki.Modules.Identity.Domain.Roles;

namespace Comuki.Modules.Identity.Domain.Assignments;

/// <summary>
/// Escalation guard for grants (task-breakdown T4.1): a grant is legal
/// only when the target role's seniority is at or below the granter's own
/// highest seniority. Equal seniority is allowed — the bootstrap
/// platform-admin must be able to re-grant its own role once seeded.
/// The seniority ladder itself is <see cref="RoleMatrix.SeniorityOf"/>.
/// </summary>
public static class RoleSeniorityGuard
{
    /// <summary>Whether the grant is legal for a granter of the given seniority.</summary>
    /// <param name="target"></param>
    /// <param name="granterSeniority">The granter's highest role seniority (0 when the granter holds no roles).</param>
    /// <returns></returns>
    public static bool CanGrant(Role target, int granterSeniority)
    {
        return RoleMatrix.SeniorityOf(target) <= granterSeniority;
    }

    /// <summary>Throws when the grant would escalate above the granter's seniority.</summary>
    /// <param name="target"></param>
    /// <param name="granterSeniority"></param>
    /// <exception cref="InvalidOperationException">The target role is senior to the granter.</exception>
    public static void EnsureGrantable(Role target, int granterSeniority)
    {
        if (!CanGrant(target, granterSeniority))
        {
            throw new InvalidOperationException(
                $"cannot grant role '{RoleKeys.Key(target)}' (seniority {RoleMatrix.SeniorityOf(target)}) "
                + $"from seniority {granterSeniority}: escalation above own seniority is not allowed");
        }
    }
}
