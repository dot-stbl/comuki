using Comuki.Modules.Identity.Domain.Permissions;
using Comuki.Modules.Identity.Domain.Roles;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Identity.Unit;

/// <summary>
/// Matrix invariants (T4.7): every vocabulary key is granted by at least
/// one role, the matrix references no key outside the vocabulary, keys
/// round-trip through <see cref="RoleKeys"/>, and the seniority ladder
/// is a strict order with platform-admin on top.
/// </summary>
public sealed class RoleMatrixShould
{
    [Fact(DisplayName = "Given the permission vocabulary, when the matrix is inspected, then every declared key is granted by at least one role")]
    public void AssignEveryPermissionToAtLeastOneRole()
    {
        var vocabulary = typeof(Permissions)
            .GetFields()
            .Select(static field => (PermissionKey)field.GetValue(null)!)
            .ToHashSet();

        vocabulary.Count.ShouldBe(23);
        vocabulary.ShouldAllBe(static key => RoleMatrix.AllPermissionKeys.Contains(key));
        RoleMatrix.AllPermissionKeys.Count.ShouldBe(vocabulary.Count);
    }

    [Fact(DisplayName = "Given all roles, when permissions are inspected, then every role has a non-empty permission set")]
    public void GiveEveryRolePermissions()
    {
        foreach (var role in Enum.GetValues<Role>())
        {
            RoleMatrix.PermissionsOf(role).ShouldNotBeEmpty($"role {role} grants nothing");
        }
    }

    [Fact(DisplayName = "Given role keys, when parsed back, then every role round-trips")]
    public void RoundTripRoleKeys()
    {
        foreach (var role in Enum.GetValues<Role>())
        {
            var key = RoleKeys.Key(role);
            RoleKeys.Parse(key).ShouldBe(role);
        }

        RoleKeys.Parse("no-such-role").ShouldBeNull();
    }

    [Fact(DisplayName = "Given the seniority ladder, when inspected, then seniorities are distinct and platform-admin is the top")]
    public void BuildStrictSeniorityLadder()
    {
        var seniorities = Enum.GetValues<Role>()
            .Select(static role => RoleMatrix.SeniorityOf(role))
            .ToHashSet();

        seniorities.Count.ShouldBe(Enum.GetValues<Role>().Length);
        RoleMatrix.SeniorityOf(Role.PlatformAdmin).ShouldBeGreaterThan(RoleMatrix.SeniorityOf(Role.Operator));
        RoleMatrix.SeniorityOf(Role.Operator).ShouldBeGreaterThan(RoleMatrix.SeniorityOf(Role.ProjectAdmin));
        RoleMatrix.SeniorityOf(Role.ProjectAdmin).ShouldBeGreaterThan(RoleMatrix.SeniorityOf(Role.Approver));
        RoleMatrix.SeniorityOf(Role.Approver).ShouldBeGreaterThan(RoleMatrix.SeniorityOf(Role.Member));
        RoleMatrix.SeniorityOf(Role.Member).ShouldBeGreaterThan(RoleMatrix.SeniorityOf(Role.Viewer));
    }

    [Fact(DisplayName = "Given platform-admin, when permissions are inspected, then it holds every declared key")]
    public void GivePlatformAdminEverything()
    {
        var granted = RoleMatrix.PermissionsOf(Role.PlatformAdmin);

        granted.Count.ShouldBe(RoleMatrix.AllPermissionKeys.Count);
        granted.ShouldContain(Permissions.PlatformAdmin);
        granted.ShouldContain(Permissions.IdentityWrite);
        granted.ShouldContain(Permissions.KnowledgeAdmin);
    }
}
