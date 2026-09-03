using Comuki.Modules.Identity.Domain.ApiKeys;
using Comuki.Modules.Identity.Domain.Assignments;
using Comuki.Modules.Identity.Domain.Roles;
using Comuki.Modules.Identity.Domain.Scopes;
using Comuki.Modules.Identity.Domain.Subjects;
using Comuki.Modules.Identity.Domain.Users;
using Comuki.Modules.Identity.Infrastructure.Persistence;
using Comuki.Modules.Identity.Infrastructure.Persistence.Stores;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace Comuki.Modules.Identity.Integration.Stores;

/// <summary>
/// Identity EF stores over a real Testcontainers Postgres: exercises the
/// truth-table branches the unit suite cannot reach — the
/// detached-vs-tracked SaveAsync paths, the active-only filter and the
/// subject/scope filter composition of <see cref="RoleAssignmentStore"/>,
/// plus the prefix lookup and SaveAsync paths of <see cref="ApiKeyStore"/>.
/// </summary>
public sealed class IdentityStoresShould : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    private IdentityDbContext db = null!;
    private RoleAssignmentStore assignmentStore = null!;
    private ApiKeyStore apiKeyStore = null!;

    private readonly DateTimeOffset now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await container.StartAsync(cancellationToken);
        var connectionString = container.GetConnectionString();

        var optionsBuilder = new DbContextOptionsBuilder<IdentityDbContext>();
        IdentityDbContext.ApplyOptions(optionsBuilder, connectionString);

        db = new IdentityDbContext(optionsBuilder.Options);
        await db.Database.MigrateAsync(cancellationToken);

        assignmentStore = new RoleAssignmentStore(db);
        apiKeyStore = new ApiKeyStore(db);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await db.DisposeAsync();
        await container.DisposeAsync();
    }

    /// <summary>Wipes the data between tests so each one starts from a clean slate.</summary>
    private async Task ResetAsync()
    {
        await db.RoleAssignments.ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        await db.ApiKeys.ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        await db.Users.ExecuteDeleteAsync(TestContext.Current.CancellationToken);
    }

    private static User NewUser()
    {
        return User.Create(
            $"u-{Guid.NewGuid():N}@example.test",
            "ada",
            "secret-12345",
            DateTimeOffset.UtcNow);
    }

    [Fact(DisplayName = "Given assignments of one subject, when ListActiveAsync runs, then only active rows return and revoked ones are filtered out")]
    public async Task ListActiveFiltersRevokedAsync()
    {
        await ResetAsync();
        var subject = new RoleSubject(SubjectType.User, Guid.NewGuid());
        var active = RoleAssignment.Create(subject, Role.Member, AssignmentScope.Platform(), null, now);
        var revoked = RoleAssignment.Create(subject, Role.Member, AssignmentScope.Platform(), null, now);
        revoked.Revoke(now);
        await db.RoleAssignments.AddRangeAsync(active, revoked);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var listed = await assignmentStore.ListActiveAsync(subject, TestContext.Current.CancellationToken);

        listed.ShouldHaveSingleItem();
        listed[0].Id.ShouldBe(active.Id);
        listed[0].IsActive.ShouldBeTrue();
    }

    [Fact(DisplayName = "Given a revoked assignment, when FindActiveAsync is queried by id, then null is returned")]
    public async Task FindActiveByIdSkipsRevokedAsync()
    {
        await ResetAsync();
        var assignment = RoleAssignment.Create(
            new RoleSubject(SubjectType.User, Guid.NewGuid()),
            Role.Member,
            AssignmentScope.Platform(),
            null,
            now);
        assignment.Revoke(now);
        await assignmentStore.SaveAsync(assignment, TestContext.Current.CancellationToken);

        var found = await assignmentStore.FindActiveAsync(assignment.Id, TestContext.Current.CancellationToken);

        found.ShouldBeNull();
    }

    [Fact(DisplayName = "Given assignments of different scopes, when FindActiveAsync runs by subject+role+scope, then only the matching one is returned")]
    public async Task FindActiveBySubjectRoleScopeAsync()
    {
        await ResetAsync();
        var subject = new RoleSubject(SubjectType.User, Guid.NewGuid());
        var projectA = ProjectId.New();
        var projectB = ProjectId.New();
        var platform = RoleAssignment.Create(subject, Role.Member, AssignmentScope.Platform(), null, now);
        var projectAA = RoleAssignment.Create(subject, Role.Member, AssignmentScope.ForProject(projectA), null, now);
        var projectBA = RoleAssignment.Create(subject, Role.Member, AssignmentScope.ForProject(projectB), null, now);
        await db.RoleAssignments.AddRangeAsync(platform, projectAA, projectBA);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var found = await assignmentStore.FindActiveAsync(
            subject,
            Role.Member,
            AssignmentScope.ForProject(projectA),
            TestContext.Current.CancellationToken);

        found.ShouldNotBeNull();
        found.Id.ShouldBe(projectAA.Id);
        found.ScopeProjectId.ShouldBe(projectA);
    }

    [Fact(DisplayName = "Given a tracked assignment, when SaveAsync runs again, then no duplicate row is inserted")]
    public async Task SaveAsyncOnTrackedUpdatesExistingAsync()
    {
        await ResetAsync();
        var assignment = RoleAssignment.Create(
            new RoleSubject(SubjectType.User, Guid.NewGuid()),
            Role.Member,
            AssignmentScope.Platform(),
            null,
            now);
        await assignmentStore.SaveAsync(assignment, TestContext.Current.CancellationToken);

        var beforeCount = await db.RoleAssignments.CountAsync(TestContext.Current.CancellationToken);
        beforeCount.ShouldBe(1);

        // Second save hits the "tracked" branch — SaveAsync skips AddAsync
        // because the entity is already attached. No mutation needed; the
        // assertion is on the row count.
        await assignmentStore.SaveAsync(assignment, TestContext.Current.CancellationToken);

        var afterCount = await db.RoleAssignments.CountAsync(TestContext.Current.CancellationToken);
        afterCount.ShouldBe(1);
    }

    [Fact(DisplayName = "Given a stored API key prefix, when FindByPrefixAsync runs, then the row is returned")]
    public async Task ApiKeyFindByPrefixAsync()
    {
        await ResetAsync();
        var user = NewUser();
        await db.Users.AddAsync(user, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var key = ApiKey.Create(user.Id, "test", "pref001", "hmac-bytes", now);
        await apiKeyStore.SaveAsync(key, TestContext.Current.CancellationToken);

        var found = await apiKeyStore.FindByPrefixAsync("pref001", TestContext.Current.CancellationToken);

        found.ShouldNotBeNull();
        found.Id.ShouldBe(key.Id);
    }

    [Fact(DisplayName = "Given an unknown prefix, when FindByPrefixAsync runs, then null is returned")]
    public async Task ApiKeyFindByPrefixMissesAsync()
    {
        await ResetAsync();

        var found = await apiKeyStore.FindByPrefixAsync("ghost", TestContext.Current.CancellationToken);

        found.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a stored API key, when FindByIdAsync runs, then the row is returned")]
    public async Task ApiKeyFindByIdAsync()
    {
        await ResetAsync();
        var user = NewUser();
        await db.Users.AddAsync(user, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var key = ApiKey.Create(user.Id, "test", "pref002", "hmac", now);
        await apiKeyStore.SaveAsync(key, TestContext.Current.CancellationToken);

        var found = await apiKeyStore.FindByIdAsync(key.Id, TestContext.Current.CancellationToken);

        found.ShouldNotBeNull();
        found.Prefix.ShouldBe("pref002");
    }

    [Fact(DisplayName = "Given a tracked API key, when SaveAsync runs again, then no duplicate row is inserted")]
    public async Task ApiKeySaveAsyncOnTrackedUpdatesAsync()
    {
        await ResetAsync();
        var user = NewUser();
        await db.Users.AddAsync(user, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var key = ApiKey.Create(user.Id, "test", "pref003", "hmac", now);
        await apiKeyStore.SaveAsync(key, TestContext.Current.CancellationToken);

        key.MarkUsed(now);
        await apiKeyStore.SaveAsync(key, TestContext.Current.CancellationToken);

        var count = await db.ApiKeys.CountAsync(TestContext.Current.CancellationToken);
        count.ShouldBe(1);
        var stored = await db.ApiKeys.FirstAsync(TestContext.Current.CancellationToken);
        stored.LastUsedAt.ShouldBe(now);
    }
}
