using Comuki.Modules.Identity.Application.Authorization;
using Comuki.Modules.Identity.Application.Ports;
using Comuki.Modules.Identity.Domain.Assignments;
using Comuki.Modules.Identity.Domain.Ids;
using Comuki.Modules.Identity.Domain.Permissions;
using Comuki.Modules.Identity.Domain.Roles;
using Comuki.Modules.Identity.Domain.Scopes;
using Comuki.Modules.Identity.Domain.Subjects;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Identity.Unit;

/// <summary>
/// Evaluator semantics (T4.4): platform grants apply everywhere, project
/// grants only inside their project, no assignments is the fail-closed
/// empty answer, and the 30s cache hits the store once until
/// invalidated.
/// </summary>
public sealed class PermissionEvaluatorShould
{
    private readonly IRoleAssignmentStore assignments = Substitute.For<IRoleAssignmentStore>();
    private readonly PermissionEvaluator evaluator;

    public PermissionEvaluatorShould()
    {
        evaluator = new PermissionEvaluator(new MemoryCache(new MemoryCacheOptions()), assignments);
    }

    [Fact(DisplayName = "Given a platform-scope grant, when evaluated, then the permissions apply everywhere")]
    public async Task ApplyPlatformGrantsGloballyAsync()
    {
        var subject = RoleSubject.ForUser(UserId.New());
        var grant = RoleAssignment.Create(subject, Role.PlatformAdmin, AssignmentScope.Platform(), null, DateTimeOffset.UtcNow);
        _ = assignments.ListActiveAsync(subject, TestContext.Current.CancellationToken).Returns([grant]);

        var authorization = await evaluator.EvaluateAsync(subject, TestContext.Current.CancellationToken);

        authorization.IsPermittedGlobally(Permissions.IdentityWrite).ShouldBeTrue();
        authorization.IsPermitted(Permissions.RunStop).ShouldBeTrue();
        authorization.IsPermittedIn(Permissions.PlatformAdmin, ProjectId.New()).ShouldBeTrue();
        authorization.PlatformPermissions.Count.ShouldBe(RoleMatrix.PermissionsOf(Role.PlatformAdmin).Count);
        authorization.ProjectPermissions.Count.ShouldBe(0);
    }

    [Fact(DisplayName = "Given a project-scope grant, when evaluated, then the permissions apply only inside that project")]
    public async Task ScopeProjectGrantsToTheirProjectAsync()
    {
        var project = ProjectId.New();
        var otherProject = ProjectId.New();
        var subject = RoleSubject.ForUser(UserId.New());
        var grant = RoleAssignment.Create(subject, Role.Viewer, AssignmentScope.ForProject(project), null, DateTimeOffset.UtcNow);
        _ = assignments.ListActiveAsync(subject, TestContext.Current.CancellationToken).Returns([grant]);

        var authorization = await evaluator.EvaluateAsync(subject, TestContext.Current.CancellationToken);

        authorization.IsPermittedGlobally(Permissions.RunRead).ShouldBeFalse();
        authorization.IsPermitted(Permissions.RunRead).ShouldBeTrue();
        authorization.IsPermittedIn(Permissions.RunRead, project).ShouldBeTrue();
        authorization.IsPermittedIn(Permissions.RunRead, otherProject).ShouldBeFalse();
        authorization.IsPermitted(Permissions.RunCreate).ShouldBeFalse();
        authorization.IsPermitted(Permissions.IdentityWrite).ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a subject with no assignments, when evaluated, then the answer is empty on both axes")]
    public async Task ReturnEmptyForSubjectWithoutAssignmentsAsync()
    {
        var subject = RoleSubject.ForApiKey(ApiKeyId.New());
        _ = assignments.ListActiveAsync(subject, TestContext.Current.CancellationToken).Returns([]);

        var authorization = await evaluator.EvaluateAsync(subject, TestContext.Current.CancellationToken);

        authorization.IsPermitted(Permissions.RunRead).ShouldBeFalse();
        authorization.IsPermittedIn(Permissions.RunRead, ProjectId.New()).ShouldBeFalse();
        authorization.PlatformPermissions.Count.ShouldBe(0);
        authorization.ProjectPermissions.Count.ShouldBe(0);
    }

    [Fact(DisplayName = "Given a cached evaluation, when the same subject is evaluated again, then the store is read once")]
    public async Task CachePerSubjectAsync()
    {
        var subject = RoleSubject.ForUser(UserId.New());
        var grant = RoleAssignment.Create(subject, Role.Member, AssignmentScope.Platform(), null, DateTimeOffset.UtcNow);
        _ = assignments.ListActiveAsync(subject, TestContext.Current.CancellationToken).Returns([grant]);

        _ = await evaluator.EvaluateAsync(subject, TestContext.Current.CancellationToken);
        _ = await evaluator.EvaluateAsync(subject, TestContext.Current.CancellationToken);

        _ = await assignments.Received(1).ListActiveAsync(subject, TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "Given a cached evaluation, when the subject is invalidated, then the next evaluation re-reads the store")]
    public async Task ReReadAfterInvalidateAsync()
    {
        var subject = RoleSubject.ForUser(UserId.New());
        var grant = RoleAssignment.Create(subject, Role.Member, AssignmentScope.Platform(), null, DateTimeOffset.UtcNow);
        _ = assignments.ListActiveAsync(subject, TestContext.Current.CancellationToken).Returns([grant]);

        _ = await evaluator.EvaluateAsync(subject, TestContext.Current.CancellationToken);
        evaluator.Invalidate(subject);
        _ = await evaluator.EvaluateAsync(subject, TestContext.Current.CancellationToken);

        _ = await assignments.Received(2).ListActiveAsync(subject, TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "Given two subjects, when one is invalidated, then the other keeps its cache entry")]
    public async Task InvalidateOnlyTheNamedSubjectAsync()
    {
        var first = RoleSubject.ForUser(UserId.New());
        var second = RoleSubject.ForUser(UserId.New());
        var firstGrant = RoleAssignment.Create(first, Role.Member, AssignmentScope.Platform(), null, DateTimeOffset.UtcNow);
        var secondGrant = RoleAssignment.Create(second, Role.Member, AssignmentScope.Platform(), null, DateTimeOffset.UtcNow);
        _ = assignments.ListActiveAsync(first, TestContext.Current.CancellationToken).Returns([firstGrant]);
        _ = assignments.ListActiveAsync(second, TestContext.Current.CancellationToken).Returns([secondGrant]);

        _ = await evaluator.EvaluateAsync(first, TestContext.Current.CancellationToken);
        _ = await evaluator.EvaluateAsync(second, TestContext.Current.CancellationToken);
        evaluator.Invalidate(first);
        _ = await evaluator.EvaluateAsync(first, TestContext.Current.CancellationToken);
        _ = await evaluator.EvaluateAsync(second, TestContext.Current.CancellationToken);

        _ = await assignments.Received(2).ListActiveAsync(first, TestContext.Current.CancellationToken);
        _ = await assignments.Received(1).ListActiveAsync(second, TestContext.Current.CancellationToken);
    }
}
