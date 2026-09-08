using System.Collections.Frozen;
using System.Security.Claims;
using Comuki.Host.Auth.Security;
using Comuki.Modules.Identity.Application.Authorization;
using Comuki.Modules.Identity.Domain.Permissions;
using Comuki.Modules.Identity.Domain.Subjects;
using Comuki.Modules.Identity.Infrastructure.Security;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.Auth;

/// <summary>
/// Unit tests for <see cref="SubjectScopeMiddleware"/>: the middleware is the
/// single place that turns an authenticated principal into the ambient
/// subject scope — anonymous calls get the fail-closed
/// <see cref="SubjectScope.Nothing"/> for the duration of the request,
/// authenticated calls get the scope derived from the permission evaluator.
/// AsyncLocal isolation must hold across concurrent requests.
/// </summary>
public sealed class SubjectScopeMiddlewareShould
{
    [Fact(DisplayName = "Given an authenticated user, when InvokeAsync runs, then the scope from the evaluator is set on the accessor")]
    public async Task AuthenticatedRequestEstablishesResolvedScopeAsync()
    {
        var accessor = new AsyncLocalSubjectScopeAccessor();
        var evaluator = Substitute.For<IPermissionEvaluator>();
        var userId = Guid.NewGuid();
        var project = ProjectId.New();
        var authorization = new SubjectAuthorization(
            FrozenSet<PermissionKey>.Empty,
            new Dictionary<ProjectId, IReadOnlySet<PermissionKey>> { [project] = FrozenSet<PermissionKey>.Empty }
                .ToFrozenDictionary(static pair => pair.Key, static pair => pair.Value));
        var observed = new TaskCompletionSource<SubjectScope?>();
        _ = evaluator.EvaluateAsync(Arg.Any<RoleSubject>(), Arg.Any<CancellationToken>())
            .Returns(authorization);
        var middleware = new SubjectScopeMiddleware(SnapshotScopeAsync(accessor, observed));
        var context = NewContext(
            NewPrincipal(ClaimTypes.NameIdentifier, userId.ToString()),
            BuildServices(accessor, evaluator));

        await middleware.InvokeAsync(context);

        var observedScope = await observed.Task;
        observedScope.ShouldNotBeNull();
        observedScope.Unrestricted.ShouldBeFalse();
        observedScope.ProjectIds.ShouldContain(project);
    }

    [Fact(DisplayName = "Given an anonymous principal, when InvokeAsync runs, then the Nothing scope is set for the duration of the request")]
    public async Task UnauthenticatedRequestEstablishesNothingScopeAsync()
    {
        var accessor = new AsyncLocalSubjectScopeAccessor();
        var evaluator = Substitute.For<IPermissionEvaluator>();
        var observed = new TaskCompletionSource<SubjectScope?>();
        var middleware = new SubjectScopeMiddleware(SnapshotScopeAsync(accessor, observed));
        var context = NewContext(
            new ClaimsPrincipal(new ClaimsIdentity()),
            BuildServices(accessor, evaluator));

        await middleware.InvokeAsync(context);

        var observedScope = await observed.Task;
        observedScope.ShouldBe(SubjectScope.Nothing);
        await evaluator.DidNotReceive().EvaluateAsync(Arg.Any<RoleSubject>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a downstream that throws, when InvokeAsync runs, then the scope handle is still disposed")]
    public async Task DownstreamThrowStillDisposesScopeAsync()
    {
        var accessor = new AsyncLocalSubjectScopeAccessor();
        var evaluator = Substitute.For<IPermissionEvaluator>();
        var userId = Guid.NewGuid();
        _ = evaluator.EvaluateAsync(Arg.Any<RoleSubject>(), Arg.Any<CancellationToken>())
            .Returns(SubjectAuthorization.Empty);
        var middleware = new SubjectScopeMiddleware(ThrowingNextAsync);
        var context = NewContext(
            NewPrincipal(ClaimTypes.NameIdentifier, userId.ToString()),
            BuildServices(accessor, evaluator));

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await middleware.InvokeAsync(context));

        accessor.CurrentOrNone.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a prior AsSystem scope, when InvokeAsync runs and exits, then the prior scope is restored")]
    public async Task AsSystemOverrideIsRestoredOnExitAsync()
    {
        var accessor = new AsyncLocalSubjectScopeAccessor();
        var evaluator = Substitute.For<IPermissionEvaluator>();
        var userId = Guid.NewGuid();
        _ = evaluator.EvaluateAsync(Arg.Any<RoleSubject>(), Arg.Any<CancellationToken>())
            .Returns(SubjectAuthorization.Empty);
        var middleware = new SubjectScopeMiddleware(NoopNextAsync);
        var context = NewContext(
            NewPrincipal(ClaimTypes.NameIdentifier, userId.ToString()),
            BuildServices(accessor, evaluator));

        using (accessor.AsSystem("outer-consumer"))
        {
            await middleware.InvokeAsync(context);

            accessor.CurrentOrNone.ShouldNotBeNull();
            accessor.CurrentOrNone.SystemName.ShouldBe("outer-consumer");
        }

        accessor.CurrentOrNone.ShouldBeNull();
    }

    [Fact(DisplayName = "Given two concurrent authenticated requests, when both run, then their scopes do not leak between flows")]
    public async Task ConcurrentRequestsHaveIsolatedScopesAsync()
    {
        var accessor = new AsyncLocalSubjectScopeAccessor();
        var evaluator = Substitute.For<IPermissionEvaluator>();
        var firstProject = ProjectId.New();
        var secondProject = ProjectId.New();
        var firstAuthorization = new SubjectAuthorization(
            FrozenSet<PermissionKey>.Empty,
            new Dictionary<ProjectId, IReadOnlySet<PermissionKey>> { [firstProject] = FrozenSet<PermissionKey>.Empty }
                .ToFrozenDictionary(pair => pair.Key, pair => pair.Value));
        var secondAuthorization = new SubjectAuthorization(
            FrozenSet<PermissionKey>.Empty,
            new Dictionary<ProjectId, IReadOnlySet<PermissionKey>> { [secondProject] = FrozenSet<PermissionKey>.Empty }
                .ToFrozenDictionary(pair => pair.Key, pair => pair.Value));
        var firstUser = Guid.NewGuid();
        var secondUser = Guid.NewGuid();
        _ = evaluator.EvaluateAsync(
                Arg.Is<RoleSubject>(subject => subject.Id == firstUser),
                Arg.Any<CancellationToken>())
            .Returns(firstAuthorization);
        _ = evaluator.EvaluateAsync(
                Arg.Is<RoleSubject>(subject => subject.Id == secondUser),
                Arg.Any<CancellationToken>())
            .Returns(secondAuthorization);

        SubjectScope? firstObserved = null;
        SubjectScope? secondObserved = null;

        var firstTask = Task.Run(async () =>
        {
            var observed = new TaskCompletionSource<SubjectScope?>();
            var middleware = new SubjectScopeMiddleware(SnapshotScopeAsync(accessor, observed));
            var context = NewContext(
                NewPrincipal(ClaimTypes.NameIdentifier, firstUser.ToString()),
                BuildServices(accessor, evaluator));
            await middleware.InvokeAsync(context);
            firstObserved = await observed.Task;
        }, TestContext.Current.CancellationToken);

        var secondTask = Task.Run(async () =>
        {
            var observed = new TaskCompletionSource<SubjectScope?>();
            var middleware = new SubjectScopeMiddleware(SnapshotScopeAsync(accessor, observed));
            var context = NewContext(
                NewPrincipal(ClaimTypes.NameIdentifier, secondUser.ToString()),
                BuildServices(accessor, evaluator));
            await middleware.InvokeAsync(context);
            secondObserved = await observed.Task;
        }, TestContext.Current.CancellationToken);

        await Task.WhenAll(firstTask, secondTask);

        firstObserved.ShouldNotBeNull();
        secondObserved.ShouldNotBeNull();
        firstObserved.ProjectIds.ShouldContain(firstProject);
        firstObserved.ProjectIds.ShouldNotContain(secondProject);
        secondObserved.ProjectIds.ShouldContain(secondProject);
        secondObserved.ProjectIds.ShouldNotContain(firstProject);
    }

    [Fact(DisplayName = "Given an api-key claim, when InvokeAsync runs, then the api-key subject is resolved and the scope is established")]
    public async Task ApiKeyClaimResolvesAndEstablishesScopeAsync()
    {
        var accessor = new AsyncLocalSubjectScopeAccessor();
        var evaluator = Substitute.For<IPermissionEvaluator>();
        var apiKeyId = Guid.NewGuid();
        _ = evaluator.EvaluateAsync(
                Arg.Is<RoleSubject>(subject => subject.Type == SubjectType.ApiKey && subject.Id == apiKeyId),
                Arg.Any<CancellationToken>())
            .Returns(SubjectAuthorization.Empty);
        var middleware = new SubjectScopeMiddleware(NoopNextAsync);
        var context = NewContext(
            NewPrincipal(IdentityClaimNames.ApiKeyId, apiKeyId.ToString()),
            BuildServices(accessor, evaluator));

        await middleware.InvokeAsync(context);

        await evaluator.Received(1).EvaluateAsync(
            Arg.Is<RoleSubject>(subject => subject.Type == SubjectType.ApiKey && subject.Id == apiKeyId),
            Arg.Any<CancellationToken>());
    }

    private static RequestDelegate SnapshotScopeAsync(ISubjectScopeAccessor accessor, TaskCompletionSource<SubjectScope?> sink)
    {
        return _ =>
        {
            sink.TrySetResult(accessor.CurrentOrNone);
            return Task.CompletedTask;
        };
    }

    private static Task NoopNextAsync(HttpContext _)
    {
        return Task.CompletedTask;
    }

    private static Task ThrowingNextAsync(HttpContext _)
    {
        throw new InvalidOperationException("downstream fault");
    }

    private static ClaimsPrincipal NewPrincipal(string claimType, string claimValue)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(claimType, claimValue)],
            authenticationType: "Test"));
    }

    private static HttpContext NewContext(ClaimsPrincipal user, IServiceProvider services)
    {
        var context = new DefaultHttpContext
        {
            User = user,
            RequestServices = services
        };
        return context;
    }

    private static IServiceProvider BuildServices(ISubjectScopeAccessor accessor, IPermissionEvaluator evaluator)
    {
        var services = new ServiceCollection();
        _ = services.AddSingleton(accessor);
        _ = services.AddSingleton(evaluator);
        return services.BuildServiceProvider();
    }
}
