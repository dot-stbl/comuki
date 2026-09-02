using System.Collections.Frozen;
using Comuki.Modules.Identity.Application.Authorization;
using Comuki.Modules.Identity.Domain.Permissions;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Identity.Unit;

/// <summary>
/// The projection of a subject's authorization into the object axis
/// (<see cref="SubjectAuthorization.ToSubjectScope"/>): any
/// platform-scope grant widens globally, project grants confine to their
/// projects, no assignments project to the fail-closed Nothing scope.
/// </summary>
public sealed class SubjectAuthorizationShould
{
    [Fact(DisplayName = "Given any platform-scope grant, when projected, then the scope is unrestricted")]
    public void ProjectPlatformGrantsToUnrestricted()
    {
        var authorization = new SubjectAuthorization(
            PermissionsOf(Permissions.ProjectRead),
            FrozenDictionary<ProjectId, IReadOnlySet<PermissionKey>>.Empty);

        var scope = authorization.ToSubjectScope();

        scope.Unrestricted.ShouldBeTrue();
        scope.SystemName.ShouldBeNull();
        scope.Allows(ProjectId.New()).ShouldBeTrue();
    }

    [Fact(DisplayName = "Given grants on three projects, when projected, then the scope is confined to exactly those three")]
    public void ProjectProjectGrantsToTheirProjects()
    {
        var first = ProjectId.New();
        var second = ProjectId.New();
        var third = ProjectId.New();
        var authorization = new SubjectAuthorization(
            PermissionsOf(),
            new Dictionary<ProjectId, IReadOnlySet<PermissionKey>>
            {
                [first] = PermissionsOf(Permissions.RunRead),
                [second] = PermissionsOf(Permissions.RunRead),
                [third] = PermissionsOf(Permissions.RunRead),
            }.ToFrozenDictionary());

        var scope = authorization.ToSubjectScope();

        scope.Unrestricted.ShouldBeFalse();
        scope.Allows(first).ShouldBeTrue();
        scope.Allows(second).ShouldBeTrue();
        scope.Allows(third).ShouldBeTrue();
        scope.Allows(ProjectId.New()).ShouldBeFalse();
    }

    [Fact(DisplayName = "Given no assignments, when projected, then the scope is the fail-closed Nothing")]
    public void ProjectNoAssignmentsToNothing()
    {
        var authorization = new SubjectAuthorization(
            PermissionsOf(),
            FrozenDictionary<ProjectId, IReadOnlySet<PermissionKey>>.Empty);

        var scope = authorization.ToSubjectScope();

        scope.Unrestricted.ShouldBeFalse();
        scope.ProjectIds.ShouldBeEmpty();
        scope.Allows(ProjectId.New()).ShouldBeFalse();
    }

    [Fact(DisplayName = "Given both a platform grant and project grants, when projected, then the platform grant wins globally")]
    public void PlatformGrantWinsOverProjectGrants()
    {
        var project = ProjectId.New();
        var authorization = new SubjectAuthorization(
            PermissionsOf(Permissions.RunRead),
            new Dictionary<ProjectId, IReadOnlySet<PermissionKey>>
            {
                [project] = PermissionsOf(Permissions.RunRead),
            }.ToFrozenDictionary());

        var scope = authorization.ToSubjectScope();

        scope.Unrestricted.ShouldBeTrue();
        scope.Allows(ProjectId.New()).ShouldBeTrue();
    }

    private static IReadOnlySet<PermissionKey> PermissionsOf(params PermissionKey[] keys)
    {
        return keys.ToFrozenSet();
    }
}
