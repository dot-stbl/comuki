using Comuki.Modules.Identity.Application.ApiKeys;
using Comuki.Modules.Identity.Application.ApiKeys.Issue;
using Comuki.Modules.Identity.Application.Assignments.Grant;
using Comuki.Modules.Identity.Application.Assignments.Revoke;
using Comuki.Modules.Identity.Application.Authorization;
using Comuki.Modules.Identity.Application.Options;
using Comuki.Modules.Identity.Application.Ports;
using Comuki.Modules.Identity.Application.Sessions;
using Comuki.Modules.Identity.Application.Users;
using Comuki.Modules.Identity.Domain.ApiKeys;
using Comuki.Modules.Identity.Domain.Assignments;
using Comuki.Modules.Identity.Domain.Ids;
using Comuki.Modules.Identity.Domain.Roles;
using Comuki.Modules.Identity.Domain.Scopes;
using Comuki.Modules.Identity.Domain.Subjects;
using Comuki.Modules.Identity.Domain.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Identity.Unit;

/// <summary>
/// Identity application handlers with mocked stores: create/login/grant/
/// revoke/issue paths including failure codes and seniority bypass.
/// </summary>
public sealed class IdentityHandlersShould
{
    private readonly DateTimeOffset now = new(2026, 9, 1, 15, 0, 0, TimeSpan.Zero);
    private readonly FakeTime clock;
    private readonly PasswordHasher<User> passwordHasher = new();

    public IdentityHandlersShould()
    {
        clock = new FakeTime(now);
    }

    [Fact(DisplayName = "Given a free email, when CreateUser runs, then the account is saved and viewed")]
    public async Task CreateUserPersistsAsync()
    {
        var store = Substitute.For<IUserAccountStore>();
        store.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);
        var handler = new CreateUserHandler(store, passwordHasher, clock);

        var view = await handler.HandleAsync(
            new CreateUserCommand("Ada@Example.COM", " Ada ", "password1"),
            TestContext.Current.CancellationToken);

        view.Email.ShouldBe("ada@example.com");
        view.DisplayName.ShouldBe("Ada");
        await store.Received(1).SaveAsync(
            Arg.Is<User>(static user => user.Email == "ada@example.com" && user.PasswordHash != null),
            Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a taken email, when CreateUser runs, then InvalidOperationException is thrown")]
    public async Task CreateUserRefusesDuplicateAsync()
    {
        var existing = User.Create("ada@example.com", "Ada", "hash", now);
        var store = Substitute.For<IUserAccountStore>();
        store.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(existing);
        var handler = new CreateUserHandler(store, passwordHasher, clock);

        await Should.ThrowAsync<InvalidOperationException>(
            () => handler.HandleAsync(new CreateUserCommand("ada@example.com", "Ada", "password1"), TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "Given valid credentials, when Login runs, then Success carries user id and stamp")]
    public async Task LoginSucceedsAsync()
    {
        var user = User.Create("ada@example.com", "Ada", null, now);
        user.SetPassword(passwordHasher.HashPassword(user, "password1"), now);
        var store = Substitute.For<IUserAccountStore>();
        store.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);
        var handler = new LoginHandler(store, passwordHasher);

        var result = await handler.HandleAsync(new LoginCommand("ada@example.com", "password1"), TestContext.Current.CancellationToken);

        result.Success.ShouldBeTrue();
        result.UserId.ShouldBe(user.Id);
        result.TokensVersion.ShouldBe(user.TokensVersion);
    }

    [Fact(DisplayName = "Given unknown email, when Login runs, then invalid_credentials is returned")]
    public async Task LoginUnknownEmailAsync()
    {
        var store = Substitute.For<IUserAccountStore>();
        store.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);
        var handler = new LoginHandler(store, passwordHasher);

        var result = await handler.HandleAsync(new LoginCommand("missing@example.com", "x"), TestContext.Current.CancellationToken);

        result.Success.ShouldBeFalse();
        result.FailureCode.ShouldBe(LoginResult.FailureInvalidCredentials);
    }

    [Fact(DisplayName = "Given a disabled user, when Login runs, then user_disabled is returned")]
    public async Task LoginDisabledAsync()
    {
        var user = User.Create("ada@example.com", "Ada", passwordHasher.HashPassword(null!, "password1"), now);
        user.Disable(now);
        var store = Substitute.For<IUserAccountStore>();
        store.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);
        var handler = new LoginHandler(store, passwordHasher);

        var result = await handler.HandleAsync(new LoginCommand("ada@example.com", "password1"), TestContext.Current.CancellationToken);

        result.FailureCode.ShouldBe(LoginResult.FailureDisabled);
    }

    [Fact(DisplayName = "Given an OIDC-only user, when Login runs, then no_password is returned")]
    public async Task LoginOidcOnlyAsync()
    {
        var user = User.Create("ada@example.com", "Ada", null, now);
        var store = Substitute.For<IUserAccountStore>();
        store.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);
        var handler = new LoginHandler(store, passwordHasher);

        var result = await handler.HandleAsync(new LoginCommand("ada@example.com", "whatever"), TestContext.Current.CancellationToken);

        result.FailureCode.ShouldBe(LoginResult.FailureNoPassword);
    }

    [Fact(DisplayName = "Given a wrong password, when Login runs, then invalid_credentials is returned")]
    public async Task LoginWrongPasswordAsync()
    {
        var user = User.Create("ada@example.com", "Ada", passwordHasher.HashPassword(null!, "password1"), now);
        var store = Substitute.For<IUserAccountStore>();
        store.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);
        var handler = new LoginHandler(store, passwordHasher);

        var result = await handler.HandleAsync(new LoginCommand("ada@example.com", "nope"), TestContext.Current.CancellationToken);

        result.FailureCode.ShouldBe(LoginResult.FailureInvalidCredentials);
    }

    [Fact(DisplayName = "Given no ActingAs, when GrantRole runs, then seniority is bypassed and assignment saved")]
    public async Task GrantWithoutActingAsAsync()
    {
        var assignments = Substitute.For<IRoleAssignmentStore>();
        var evaluator = Substitute.For<IPermissionEvaluator>();
        assignments.FindActiveAsync(
            Arg.Any<RoleSubject>(),
            Arg.Any<Role>(),
            Arg.Any<AssignmentScope>(),
            Arg.Any<CancellationToken>()).Returns((RoleAssignment?)null);
        var grantee = RoleSubject.ForUser(UserId.New());
        var handler = new GrantRoleHandler(assignments, evaluator, clock);

        var view = await handler.HandleAsync(
            new GrantRoleCommand(grantee, Role.PlatformAdmin, AssignmentScope.Platform(), ActingAs: null),
            TestContext.Current.CancellationToken);

        view.Role.ShouldBe(RoleKeys.Key(Role.PlatformAdmin));
        await assignments.Received(1).SaveAsync(Arg.Any<RoleAssignment>(), Arg.Any<CancellationToken>());
        evaluator.Received(1).Invalidate(grantee);
    }

    [Fact(DisplayName = "Given an existing active assignment, when GrantRole runs, then InvalidOperationException is thrown")]
    public async Task GrantRefusesDuplicateAsync()
    {
        var assignments = Substitute.For<IRoleAssignmentStore>();
        var evaluator = Substitute.For<IPermissionEvaluator>();
        var grantee = RoleSubject.ForUser(UserId.New());
        var scope = AssignmentScope.Platform();
        var existing = RoleAssignment.Create(grantee, Role.Member, scope, null, now);
        assignments.FindActiveAsync(grantee, Role.Member, scope, Arg.Any<CancellationToken>()).Returns(existing);
        var handler = new GrantRoleHandler(assignments, evaluator, clock);

        await Should.ThrowAsync<InvalidOperationException>(
            () => handler.HandleAsync(
                new GrantRoleCommand(grantee, Role.Member, scope, null),
                TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "Given an active assignment, when RevokeRole runs, then it is revoked and cache invalidated")]
    public async Task RevokePersistsAsync()
    {
        var assignments = Substitute.For<IRoleAssignmentStore>();
        var evaluator = Substitute.For<IPermissionEvaluator>();
        var grantee = RoleSubject.ForUser(UserId.New());
        var assignment = RoleAssignment.Create(grantee, Role.Member, AssignmentScope.Platform(), null, now);
        assignments.FindActiveAsync(assignment.Id, Arg.Any<CancellationToken>()).Returns(assignment);
        var handler = new RevokeRoleHandler(assignments, evaluator, clock);

        var view = await handler.HandleAsync(new RevokeRoleCommand(assignment.Id, ActingAs: null), TestContext.Current.CancellationToken);

        view.RevokedAt.ShouldNotBeNull();
        await assignments.Received(1).SaveAsync(assignment, Arg.Any<CancellationToken>());
        evaluator.Received(1).Invalidate(grantee);
    }

    [Fact(DisplayName = "Given a missing assignment, when RevokeRole runs, then InvalidOperationException is thrown")]
    public async Task RevokeMissingThrowsAsync()
    {
        var assignments = Substitute.For<IRoleAssignmentStore>();
        var evaluator = Substitute.For<IPermissionEvaluator>();
        assignments.FindActiveAsync(Arg.Any<RoleAssignmentId>(), Arg.Any<CancellationToken>()).Returns((RoleAssignment?)null);
        var handler = new RevokeRoleHandler(assignments, evaluator, clock);

        await Should.ThrowAsync<InvalidOperationException>(
            () => handler.HandleAsync(new RevokeRoleCommand(RoleAssignmentId.New(), null), TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "Given an enabled user, when IssueApiKey runs, then issuer is delegated")]
    public async Task IssueApiKeyDelegatesAsync()
    {
        var userStore = Substitute.For<IUserAccountStore>();
        var apiKeyStore = Substitute.For<IApiKeyStore>();
        var user = User.Create("ada@example.com", "Ada", "hash", now);
        userStore.FindByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        var hasher = new ApiKeyHasher(Options.Create(new ApiKeyOptions { Pepper = "unit-test-pepper-0123456789abcdef" }));
        var issuer = new ApiKeyIssuer(apiKeyStore, hasher, clock);
        var handler = new IssueApiKeyHandler(userStore, issuer);

        var credential = await handler.HandleAsync(new IssueApiKeyCommand(user.Id, "ci"), TestContext.Current.CancellationToken);

        credential.Name.ShouldBe("ci");
        await apiKeyStore.Received(1).SaveAsync(Arg.Any<ApiKey>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a disabled user, when IssueApiKey runs, then InvalidOperationException is thrown")]
    public async Task IssueApiKeyRefusesDisabledAsync()
    {
        var userStore = Substitute.For<IUserAccountStore>();
        var user = User.Create("ada@example.com", "Ada", "hash", now);
        user.Disable(now);
        userStore.FindByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        var handler = new IssueApiKeyHandler(
            userStore,
            new ApiKeyIssuer(
                Substitute.For<IApiKeyStore>(),
                new ApiKeyHasher(Options.Create(new ApiKeyOptions { Pepper = "unit-test-pepper-0123456789abcdef" })),
                clock));

        await Should.ThrowAsync<InvalidOperationException>(
            () => handler.HandleAsync(new IssueApiKeyCommand(user.Id, "ci"), TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "Given a missing user, when IssueApiKey runs, then InvalidOperationException is thrown")]
    public async Task IssueApiKeyMissingUserAsync()
    {
        var userStore = Substitute.For<IUserAccountStore>();
        userStore.FindByIdAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>()).Returns((User?)null);
        var handler = new IssueApiKeyHandler(
            userStore,
            new ApiKeyIssuer(
                Substitute.For<IApiKeyStore>(),
                new ApiKeyHasher(Options.Create(new ApiKeyOptions { Pepper = "unit-test-pepper-0123456789abcdef" })),
                clock));

        await Should.ThrowAsync<InvalidOperationException>(
            () => handler.HandleAsync(new IssueApiKeyCommand(UserId.New(), "ci"), TestContext.Current.CancellationToken));
    }

    private sealed class FakeTime(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
