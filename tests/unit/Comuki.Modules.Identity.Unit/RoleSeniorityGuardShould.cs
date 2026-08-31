using Comuki.Modules.Identity.Domain.Assignments;
using Comuki.Modules.Identity.Domain.Roles;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Identity.Unit;

/// <summary>
/// Escalation guard (T4.1): grants at or below the granter's own
/// seniority pass; anything above is refused — for every role pair.
/// </summary>
public sealed class RoleSeniorityGuardShould
{
    [Fact(DisplayName = "Given a granter, when the target role is junior, then the grant is allowed")]
    public void AllowJuniorGrants()
    {
        RoleSeniorityGuard.CanGrant(Role.Viewer, RoleMatrix.SeniorityOf(Role.Member)).ShouldBeTrue();
        RoleSeniorityGuard.CanGrant(Role.ProjectAdmin, RoleMatrix.SeniorityOf(Role.PlatformAdmin)).ShouldBeTrue();
    }

    [Fact(DisplayName = "Given a granter, when the target role has equal seniority, then the grant is allowed")]
    public void AllowEqualSeniorityGrants()
    {
        // The bootstrap contract: platform-admin must be able to re-grant
        // its own role once seeded from the env invite.
        RoleSeniorityGuard.CanGrant(Role.PlatformAdmin, RoleMatrix.SeniorityOf(Role.PlatformAdmin)).ShouldBeTrue();
        RoleSeniorityGuard.CanGrant(Role.Member, RoleMatrix.SeniorityOf(Role.Member)).ShouldBeTrue();
    }

    [Fact(DisplayName = "Given a granter, when the target role is senior to the granter, then EnsureGrantable throws")]
    public void RefuseEscalation()
    {
        _ = Should.Throw<InvalidOperationException>(static () =>
            RoleSeniorityGuard.EnsureGrantable(Role.PlatformAdmin, RoleMatrix.SeniorityOf(Role.Member)));
        _ = Should.Throw<InvalidOperationException>(static () =>
            RoleSeniorityGuard.EnsureGrantable(Role.Operator, RoleMatrix.SeniorityOf(Role.ProjectAdmin) - 1));
    }

    [Fact(DisplayName = "Given a granter with no roles, when any grant is attempted, then only the empty-seniority floor refuses")]
    public void RefuseEverythingForZeroSeniority()
    {
        Enum.GetValues<Role>().ShouldAllBe(static role => !RoleSeniorityGuard.CanGrant(role, 0));
    }
}
