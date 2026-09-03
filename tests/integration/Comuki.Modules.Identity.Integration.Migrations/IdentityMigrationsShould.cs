using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Modules.Identity.Application;
using Comuki.Modules.Identity.Application.Assignments.Grant;
using Comuki.Modules.Identity.Application.Assignments.Revoke;
using Comuki.Modules.Identity.Application.Authorization;
using Comuki.Modules.Identity.Application.Ports;
using Comuki.Modules.Identity.Domain.ApiKeys;
using Comuki.Modules.Identity.Domain.Assignments;
using Comuki.Modules.Identity.Domain.Ids;
using Comuki.Modules.Identity.Domain.Permissions;
using Comuki.Modules.Identity.Domain.Roles;
using Comuki.Modules.Identity.Domain.Scopes;
using Comuki.Modules.Identity.Domain.Subjects;
using Comuki.Modules.Identity.Domain.Users;
using Comuki.Modules.Identity.Infrastructure;
using Comuki.Modules.Identity.Infrastructure.Persistence;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace Comuki.Modules.Identity.Integration.Migrations;

/// <summary>
/// Proves the Identity EF migrations (applied alongside the orchestration
/// context on one real Postgres) create the expected schema — tables,
/// the module-private migrations history, unique constraints (one active
/// assignment per subject+role+scope on both scope levels, unique api key
/// prefix) — and that grant/revoke through the handlers is visible to the
/// evaluator immediately (cache invalidation).
/// </summary>
public sealed class IdentityMigrationsShould : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    /// <summary>
    /// boundary: initialised in InitializeAsync before any test runs
    /// </summary>
    private ServiceProvider provider = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await container.StartAsync(cancellationToken);

        var connectionString = container.GetConnectionString();

        // The migrator's contract: both module contexts migrate the same
        // database, each with its own migrations history table.
        var orchestrationOptions = new DbContextOptionsBuilder<OrchestrationDbContext>();
        OrchestrationDbContext.ApplyOptions(orchestrationOptions, connectionString);
        await using var orchestrationDb = new OrchestrationDbContext(orchestrationOptions.Options);
        await orchestrationDb.Database.MigrateAsync(cancellationToken);

        var services = new ServiceCollection();
        _ = services.AddIdentityPersistence(connectionString);
        _ = services.AddIdentityApplication();
        provider = services.BuildServiceProvider();

        var db = provider.GetRequiredService<IdentityDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
        await db.Database.OpenConnectionAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await provider.DisposeAsync();
        await container.DisposeAsync();
    }

    [Fact(DisplayName = "Given an empty database, when both contexts migrate it, then identity and orchestration tables coexist with separate histories")]
    public async Task CreateIdentityTablesAlongsideOrchestrationAsync()
    {
        var tables = await QuerySingleColumnAsync(
            $"SELECT table_name FROM information_schema.tables "
            + $"WHERE table_schema IN ('{IdentityDatabase.Schema}', '{OrchestrationDatabase.Schema}') ORDER BY table_name");
        var histories = await QuerySingleColumnAsync(
            $"SELECT table_name FROM information_schema.tables "
            + $"WHERE table_schema IN ('{IdentityDatabase.Schema}', '{OrchestrationDatabase.Schema}') "
            + "AND table_name = '__ef_migrations_history' ORDER BY table_name");

        tables.ShouldContain(IdentityDatabase.Users);
        tables.ShouldContain(IdentityDatabase.ApiKeys);
        tables.ShouldContain(IdentityDatabase.RoleAssignments);
        tables.ShouldContain(IdentityDatabase.OidcLinks);
        tables.ShouldContain(OrchestrationDatabase.Runs);

        // per-schema migration history; orchestration uses the default name too
        histories.ShouldContain("__ef_migrations_history");
    }

    [Fact(DisplayName = "Given migrated role_assignments, when indexes are inspected, then the active-assignment unique indexes exist with their partial filters")]
    public async Task CreateActiveAssignmentUniqueIndexesAsync()
    {
        var definitions = await QuerySingleColumnAsync(
            $"SELECT indexdef FROM pg_indexes WHERE schemaname = '{IdentityDatabase.Schema}' AND tablename = '{IdentityDatabase.RoleAssignments}'");

        definitions.ShouldContain(static index => index.Contains("ix_role_assignments_active_platform")
            && index.Contains("UNIQUE")
            && index.Contains("revoked_at IS NULL")
            && index.Contains("scope_project_id IS NULL"));
        definitions.ShouldContain(static index => index.Contains("ix_role_assignments_active_project")
            && index.Contains("UNIQUE")
            && index.Contains("scope_project_id IS NOT NULL"));
        definitions.ShouldContain(static index => index.Contains("ix_role_assignments_subject"));
    }

    [Fact(DisplayName = "Given an active assignment, when the same subject+role+scope is granted again at project scope, then the unique index refuses it")]
    public async Task RefuseDuplicateActiveProjectAssignmentAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var subject = RoleSubject.ForUser(UserId.New());
        var project = ProjectId.New();

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            _ = db.RoleAssignments.Add(RoleAssignment.Create(
                subject, Role.Member, AssignmentScope.ForProject(project), null, DateTimeOffset.UtcNow));
            _ = await db.SaveChangesAsync(cancellationToken);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            _ = db.RoleAssignments.Add(RoleAssignment.Create(
                subject, Role.Member, AssignmentScope.ForProject(project), null, DateTimeOffset.UtcNow));

            _ = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(cancellationToken));
        }
    }

    [Fact(DisplayName = "Given an active platform assignment, when the same subject+role is granted again at platform scope, then the unique index refuses it")]
    public async Task RefuseDuplicateActivePlatformAssignmentAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var subject = RoleSubject.ForUser(UserId.New());

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            _ = db.RoleAssignments.Add(RoleAssignment.Create(
                subject, Role.Operator, AssignmentScope.Platform(), null, DateTimeOffset.UtcNow));
            _ = await db.SaveChangesAsync(cancellationToken);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            _ = db.RoleAssignments.Add(RoleAssignment.Create(
                subject, Role.Operator, AssignmentScope.Platform(), null, DateTimeOffset.UtcNow));

            _ = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(cancellationToken));
        }
    }

    [Fact(DisplayName = "Given a revoked assignment, when the same subject+role+scope is granted again, then the new active row is accepted")]
    public async Task AllowRegrantAfterRevokeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var subject = RoleSubject.ForUser(UserId.New());

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var first = RoleAssignment.Create(subject, Role.Viewer, AssignmentScope.Platform(), null, DateTimeOffset.UtcNow);
        first.Revoke(DateTimeOffset.UtcNow);
        _ = db.RoleAssignments.Add(first);
        _ = await db.SaveChangesAsync(cancellationToken);

        _ = db.RoleAssignments.Add(RoleAssignment.Create(subject, Role.Viewer, AssignmentScope.Platform(), null, DateTimeOffset.UtcNow));
        _ = await db.SaveChangesAsync(cancellationToken);
    }

    [Fact(DisplayName = "Given an issued api key, when another key row with the same prefix is stored, then the unique index refuses it")]
    public async Task RefuseDuplicateApiKeyPrefixAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var user = User.Create("keys@example.com", "Keys", null, DateTimeOffset.UtcNow);
        var userId = user.Id;
        _ = db.Users.Add(user);
        _ = db.ApiKeys.Add(ApiKey.Create(userId, "first", "abcd1234", new string('0', 64), DateTimeOffset.UtcNow));
        _ = await db.SaveChangesAsync(cancellationToken);

        _ = db.ApiKeys.Add(ApiKey.Create(userId, "second", "abcd1234", new string('1', 64), DateTimeOffset.UtcNow));

        _ = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(cancellationToken));
    }

    [Fact(DisplayName = "Given a grant through the handler, when the subject is evaluated and the grant revoked, then permissions appear and disappear immediately")]
    public async Task InvalidateEvaluatorCacheOnGrantAndRevokeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var subject = RoleSubject.ForUser(UserId.New());

        await using (var scope = provider.CreateAsyncScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<GrantRoleHandler>();
            var granted = await handler.HandleAsync(
                new GrantRoleCommand(subject, Role.Member, AssignmentScope.Platform(), ActingAs: null),
                cancellationToken);
            granted.IsActive.ShouldBeTrue();
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var evaluator = scope.ServiceProvider.GetRequiredService<IPermissionEvaluator>();
            var authorization = await evaluator.EvaluateAsync(subject, cancellationToken);

            authorization.IsPermitted(Permissions.RunCreate).ShouldBeTrue();
        }

        // the cached entry is warm here — the revoke below must still land
        RoleAssignment grantedAssignment;
        await using (var scope = provider.CreateAsyncScope())
        {
            var assignments = scope.ServiceProvider.GetRequiredService<IRoleAssignmentStore>();
            grantedAssignment = (await assignments.ListActiveAsync(subject, cancellationToken)).ShouldHaveSingleItem();
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<RevokeRoleHandler>();
            var revoked = await handler.HandleAsync(new RevokeRoleCommand(grantedAssignment.Id, ActingAs: null), cancellationToken);
            revoked.IsActive.ShouldBeFalse();
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var evaluator = scope.ServiceProvider.GetRequiredService<IPermissionEvaluator>();
            var authorization = await evaluator.EvaluateAsync(subject, cancellationToken);

            authorization.IsPermitted(Permissions.RunCreate).ShouldBeFalse();
            authorization.IsPermitted(Permissions.RunRead).ShouldBeFalse();
        }
    }

    [Fact(DisplayName = "Given migrated users, when columns are inspected, then ids are uuid and password_hash is nullable")]
    public async Task StoreExpectedColumnTypesAsync()
    {
        var columns = await QueryColumnsAsync(IdentityDatabase.Schema, IdentityDatabase.Users);

        columns["id"].ShouldBe(new ColumnSpec("uuid", "NO"));
        columns["email"].ShouldBe(new ColumnSpec("character varying", "NO"));
        columns["password_hash"].ShouldBe(new ColumnSpec("character varying", "YES"));
        columns["tokens_version"].ShouldBe(new ColumnSpec("integer", "NO"));
        columns["disabled"].ShouldBe(new ColumnSpec("boolean", "NO"));

        var assignmentColumns = await QueryColumnsAsync(IdentityDatabase.Schema, IdentityDatabase.RoleAssignments);
        assignmentColumns["subject_id"].ShouldBe(new ColumnSpec("uuid", "NO"));
        assignmentColumns["scope_project_id"].ShouldBe(new ColumnSpec("uuid", "YES"));
        assignmentColumns["role"].ShouldBe(new ColumnSpec("character varying", "NO"));
    }

    private async Task<List<string>> QuerySingleColumnAsync(string sql)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = db.Database.GetDbConnection();
        var rows = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    private async Task<Dictionary<string, ColumnSpec>> QueryColumnsAsync(string schema, string tableName)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = db.Database.GetDbConnection();
        var columns = new Dictionary<string, ColumnSpec>(StringComparer.Ordinal);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT column_name, data_type, is_nullable FROM information_schema.columns "
            + "WHERE table_schema = @schema AND table_name = @tableName";
        var schemaParameter = command.CreateParameter();
        schemaParameter.ParameterName = "@schema";
        schemaParameter.Value = schema;
        _ = command.Parameters.Add(schemaParameter);
        var tableNameParameter = command.CreateParameter();
        tableNameParameter.ParameterName = "@tableName";
        tableNameParameter.Value = tableName;
        _ = command.Parameters.Add(tableNameParameter);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns[reader.GetString(0)] = new ColumnSpec(reader.GetString(1), reader.GetString(2));
        }

        return columns;
    }

    private sealed record ColumnSpec(string DataType, string IsNullable);
}
