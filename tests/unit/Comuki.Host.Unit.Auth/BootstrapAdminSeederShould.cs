using Comuki.Host.Auth;
using Comuki.Modules.Identity.Application.Assignments.Grant;
using Comuki.Modules.Identity.Application.Authorization;
using Comuki.Modules.Identity.Application.Ports;
using Comuki.Modules.Identity.Application.Users;
using Comuki.Modules.Identity.Domain.Assignments;
using Comuki.Modules.Identity.Domain.Roles;
using Comuki.Modules.Identity.Domain.Scopes;
using Comuki.Modules.Identity.Domain.Subjects;
using Comuki.Modules.Identity.Domain.Users;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.Auth;

/// <summary>
/// <see cref="BootstrapAdminSeeder"/>: idempotent bootstrap pass that
/// creates the platform admin only when no account with that email
/// exists, then grants platform-admin at platform scope via the
/// system-actor path (ActingAs = null bypasses the escalation check).
/// The bootstrap options drive the email/password pair — when either is
/// unset, the seeder logs a debug message and skips. The seeder is a
/// thin coordinator: it does not hash, salt, or mutate the password
/// before delegating to <see cref="CreateUserHandler"/> (the production
/// PBKDF2 <see cref="Microsoft.AspNetCore.Identity.IPasswordHasher{TUser}"/>
/// does that work). The two handlers are sealed classes (not behind
/// ports) — the assertions run against the stores they touch: the user
/// store for the create path, the assignment store for the grant path.
/// </summary>
public sealed class BootstrapAdminSeederShould
{
    private static readonly DateTimeOffset anchorTime = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given configured credentials and no existing user, when SeedAsync runs, then a user is created with the configured email and password (not stored as plaintext)")]
    public async Task CreatesUserWhenMissingAsync()
    {
        var email = "admin@example.com";
        var password = "supersecret-bootstrap-pw";
        var fixtures = BuildFixtures(NewOptions(email, password), existingUser: null);

        await fixtures.Seeder.SeedAsync(TestContext.Current.CancellationToken);

        var savedUser = fixtures.UserStore.ReceivedCalls()
            .Select(static call => call.GetArguments()[0])
            .OfType<User>()
            .Single();
        savedUser.Email.ShouldBe(email.ToLowerInvariant());
        savedUser.PasswordHash.ShouldNotBeNull();
        savedUser.PasswordHash.ShouldNotBe(password);
    }

    [Fact(DisplayName = "Given a user with the configured email that already exists, when SeedAsync runs, then no user is saved and no assignment is saved")]
    public async Task IdempotentWhenUserExistsAsync()
    {
        var email = "admin@example.com";
        var existing = User.Create(email, email, passwordHash: "pre-existing-hash", now: anchorTime);
        var fixtures = BuildFixtures(NewOptions(email, "anything"), existingUser: existing);

        await fixtures.Seeder.SeedAsync(TestContext.Current.CancellationToken);

        await fixtures.UserStore.DidNotReceiveWithAnyArgs().SaveAsync(default!, Arg.Any<CancellationToken>());
        await fixtures.Assignments.DidNotReceiveWithAnyArgs().SaveAsync(default!, Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given configured credentials, when SeedAsync runs, then the grant uses SubjectType.User (not System), Disabled=false on the user, and the platform scope")]
    public async Task GrantsAsUserSubjectWithDisabledFalseAsync()
    {
        var email = "admin@example.com";
        var fixtures = BuildFixtures(NewOptions(email, "veryspecific-plaintext-pw-7g3hjk"), existingUser: null);

        await fixtures.Seeder.SeedAsync(TestContext.Current.CancellationToken);

        var savedAssignment = fixtures.Assignments.ReceivedCalls()
            .Select(static call => call.GetArguments()[0])
            .OfType<RoleAssignment>()
            .Single();
        savedAssignment.SubjectType.ShouldBe(SubjectType.User);
        savedAssignment.Role.ShouldBe(Role.PlatformAdmin);
        savedAssignment.ScopeLevel.ShouldBe(ScopeLevel.Platform);
        savedAssignment.GrantedBy.ShouldBeNull();

        var savedUser = fixtures.UserStore.ReceivedCalls()
            .Select(static call => call.GetArguments()[0])
            .OfType<User>()
            .Single();
        savedUser.Disabled.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given unconfigured credentials (email/password null), when SeedAsync runs, then no create, no grant, and no user-store lookup happens")]
    public async Task SkipWhenOptionsUnsetAsync()
    {
        var fixtures = BuildFixtures(
            new BootstrapAdminOptions { AdminEmail = null, AdminPassword = null },
            existingUser: null);

        await fixtures.Seeder.SeedAsync(TestContext.Current.CancellationToken);

        await fixtures.UserStore.DidNotReceiveWithAnyArgs().FindByEmailAsync(default!, Arg.Any<CancellationToken>());
        await fixtures.UserStore.DidNotReceiveWithAnyArgs().SaveAsync(default!, Arg.Any<CancellationToken>());
        await fixtures.Assignments.DidNotReceiveWithAnyArgs().SaveAsync(default!, Arg.Any<CancellationToken>());
    }

    private static BootstrapAdminOptions NewOptions(string email, string password)
    {
        return new BootstrapAdminOptions { AdminEmail = email, AdminPassword = password };
    }

    private static Seam BuildFixtures(BootstrapAdminOptions options, User? existingUser)
    {
        var userStore = Substitute.For<IUserAccountStore>();
        userStore.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(existingUser);
        var hasher = Substitute.For<Microsoft.AspNetCore.Identity.IPasswordHasher<User>>();
        // The production CreateUserHandler passes null! for the user
        // argument (the stock PasswordHasher ignores it). Match null
        // here so the substitute returns a hash rather than the default
        // null/empty string — any non-plaintext value proves the seeder
        // does not store the password itself.
        hasher.HashPassword(null!, Arg.Any<string>())
            .Returns(static call => $"hashed::{call.ArgAt<string>(1)}");
        var assignments = Substitute.For<IRoleAssignmentStore>();
        assignments.ListActiveAsync(Arg.Any<RoleSubject>(), Arg.Any<CancellationToken>())
            .Returns([]);
        assignments.FindActiveAsync(Arg.Any<RoleSubject>(), Arg.Any<Role>(), Arg.Any<AssignmentScope>(), Arg.Any<CancellationToken>())
            .Returns((RoleAssignment?)null);
        var evaluator = Substitute.For<IPermissionEvaluator>();
        var clock = TimeProvider.System;

        var createUser = new CreateUserHandler(userStore, hasher, clock);
        var grantRole = new GrantRoleHandler(assignments, evaluator, clock);
        var seeder = new BootstrapAdminSeeder(options, createUser, grantRole, userStore, NullLogger<BootstrapAdminSeeder>.Instance);
        return new Seam(seeder, userStore, assignments);
    }

    private sealed record Seam(
        BootstrapAdminSeeder Seeder,
        IUserAccountStore UserStore,
        IRoleAssignmentStore Assignments);
}
