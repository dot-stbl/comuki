using Comuki.Modules.Repositories.Application.Ports;
using Comuki.Modules.Repositories.Domain.Ids;
using Comuki.Modules.Repositories.Domain.Repositories;
using Comuki.Modules.Repositories.Infrastructure;
using Comuki.Modules.Repositories.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace Comuki.Modules.Repositories.Integration;

/// <summary>
/// Proves the Repositories EF migrations create the expected schema —
/// three tables in <c>repositories</c>, the module-private history table
/// <c>__comuki_repositories</c>, the unique (host, url) index, and the
/// right Postgres column types — and that the store round-trips the
/// (host, url) dedup contract: a second register with casing/whitespace
/// variants resolves to the existing row, a raw insert is refused by the
/// unique index, the policy <c>text[]</c> lists round-trip normalized,
/// and deleting the repository cascades policy + credential rows.
/// </summary>
public sealed class RepositoriesMigrationsShould : IAsyncLifetime
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

        var services = new ServiceCollection();
        services.AddRepositoriesModule(connectionString);
        provider = services.BuildServiceProvider();

        var db = provider.GetRequiredService<RepositoriesDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await provider.DisposeAsync();
        await container.DisposeAsync();
    }

    [Fact(DisplayName = "Given an empty database, when the repositories context migrates it, then the three tables and the private history coexist")]
    public async Task CreateRepositoriesTablesAlongsideHistoryAsync()
    {
        var tables = await QuerySingleColumnAsync(
            "SELECT table_name FROM information_schema.tables "
            + "WHERE table_schema = 'repositories' ORDER BY table_name");
        var historyTables = await QuerySingleColumnAsync(
            "SELECT table_name FROM information_schema.tables "
            + "WHERE table_schema = 'repositories' AND table_name = '__comuki_repositories'");

        tables.ShouldContain(RepositoriesDatabase.Repositories);
        tables.ShouldContain(RepositoriesDatabase.RepositoryPolicies);
        tables.ShouldContain(RepositoriesDatabase.RepositoryCredentialRefs);
        historyTables.ShouldContain("__comuki_repositories");
    }

    [Fact(DisplayName = "Given migrated repositories, when indexes are inspected, then the unique (host, url) index exists")]
    public async Task CreateUniqueHostUrlIndexAsync()
    {
        var definitions = await QuerySingleColumnAsync(
            "SELECT indexdef FROM pg_indexes "
            + "WHERE schemaname = 'repositories' AND tablename = 'repositories'");

        definitions.ShouldContain(static definition => definition.Contains("ux_repositories_host_url")
            && definition.Contains("UNIQUE")
            && definition.Contains("host")
            && definition.Contains("url"));
    }

    [Fact(DisplayName = "Given migrated repositories, when columns are inspected, then the keys are uuid and the urls are varchar(2048)")]
    public async Task StoreExpectedColumnTypesAsync()
    {
        var columns = await QueryColumnsAsync("repositories", RepositoriesDatabase.Repositories);

        columns["id"].ShouldBe(new ColumnSpec("uuid", "NO"));
        columns["url"].ShouldBe(new ColumnSpec("character varying", "NO"));
        columns["host"].ShouldBe(new ColumnSpec("character varying", "NO"));
        columns["default_branch"].ShouldBe(new ColumnSpec("character varying", "NO"));
        columns["created_at"].ShouldBe(new ColumnSpec("timestamp with time zone", "NO"));
        columns["updated_at"].ShouldBe(new ColumnSpec("timestamp with time zone", "NO"));

        var policyColumns = await QueryColumnsAsync("repositories", RepositoriesDatabase.RepositoryPolicies);
        policyColumns["repository_id"].ShouldBe(new ColumnSpec("uuid", "NO"));
        policyColumns["protected_branches"].ShouldBe(new ColumnSpec("ARRAY", "NO"));
        policyColumns["required_checks"].ShouldBe(new ColumnSpec("ARRAY", "NO"));
        policyColumns["approvers"].ShouldBe(new ColumnSpec("ARRAY", "NO"));

        var credentialColumns = await QueryColumnsAsync("repositories", RepositoriesDatabase.RepositoryCredentialRefs);
        credentialColumns["repository_id"].ShouldBe(new ColumnSpec("uuid", "NO"));
        credentialColumns["integration_ref"].ShouldBe(new ColumnSpec("character varying", "NO"));
        credentialColumns["default_access"].ShouldBe(new ColumnSpec("character varying", "NO"));
    }

    [Fact(DisplayName = "Given a stored repository, when GetOrRegister is called again with casing/whitespace variants, then the same row is returned")]
    public async Task GetOrRegisterDedupesByNormalizedIdentityAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;

        Repository first;
        await using (var scope = provider.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IRepositoryStore>();
            var candidate = Repository.Create("https://github.com/acme/app.git", "github.com", "main", now);
            var policy = RepositoryPolicy.Create(candidate.Id, ["main"], ["lint"], ["alice"], now);
            var credential = RepositoryCredentialRef.Create(candidate.Id, "integration-1", RepositoryAccess.Write, now);

            first = await store.GetOrRegisterAsync(candidate, policy, credential, cancellationToken);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IRepositoryStore>();
            var candidate = Repository.Create("  Https://GitHub.com/Acme/App.GIT ", " GitHub.COM ", "main", now);
            var policy = RepositoryPolicy.Create(candidate.Id, ["main"], ["lint"], ["alice"], now);
            var credential = RepositoryCredentialRef.Create(candidate.Id, "integration-2", RepositoryAccess.Write, now);

            var second = await store.GetOrRegisterAsync(candidate, policy, credential, cancellationToken);

            second.Id.ShouldBe(first.Id);
            second.Url.ShouldBe(first.Url);
            second.Host.ShouldBe(first.Host);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IRepositoryStore>();
            var all = await store.ListAsync(cancellationToken);

            all.ShouldHaveSingleItem();
            all[0].Id.ShouldBe(first.Id);
        }
    }

    [Fact(DisplayName = "Given a stored repository, when the same (host, url) is inserted raw, then the unique index refuses it")]
    public async Task RefuseRawDuplicateInsertAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;

        await using (var scope = provider.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IRepositoryStore>();
            var candidate = Repository.Create("https://github.com/acme/app.git", "github.com", "main", now);
            var policy = RepositoryPolicy.Create(candidate.Id, [], [], [], now);
            var credential = RepositoryCredentialRef.Create(candidate.Id, "integration-1", RepositoryAccess.Write, now);

            await store.GetOrRegisterAsync(candidate, policy, credential, cancellationToken);
        }

        await using var rawScope = provider.CreateAsyncScope();
        var db = rawScope.ServiceProvider.GetRequiredService<RepositoriesDbContext>();
        var duplicate = Repository.Create("https://github.com/acme/app.git", "github.com", "main", DateTimeOffset.UtcNow);
        db.Repositories.Add(duplicate);

        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(cancellationToken));
    }

    [Fact(DisplayName = "Given a stored repository, when FindByIdentityAsync is called with casing/whitespace variants, then it returns the same row")]
    public async Task FindByIdentityNormalizesBeforeQueryAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;

        RepositoryId storedId;
        await using (var scope = provider.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IRepositoryStore>();
            var candidate = Repository.Create("https://github.com/acme/app.git", "github.com", "main", now);
            var policy = RepositoryPolicy.Create(candidate.Id, [], [], [], now);
            var credential = RepositoryCredentialRef.Create(candidate.Id, "integration-1", RepositoryAccess.Write, now);

            var stored = await store.GetOrRegisterAsync(candidate, policy, credential, cancellationToken);
            storedId = stored.Id;
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IRepositoryStore>();

            var found = await store.FindByIdentityAsync("GitHub.COM", " Https://GitHub.com/Acme/App.GIT ", cancellationToken);
            found.ShouldNotBeNull();
            found!.Id.ShouldBe(storedId);
        }
    }

    [Fact(DisplayName = "Given a registered repository, when the policy's text[] lists are read back, then they round-trip normalized")]
    public async Task RoundTripPolicyTextArraysAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;

        await using (var scope = provider.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IRepositoryStore>();
            var candidate = Repository.Create("https://github.com/acme/app.git", "github.com", "main", now);
            var policy = RepositoryPolicy.Create(
                candidate.Id,
                protectedBranches: ["  Main ", "develop", "main"],
                requiredChecks: ["Lint", "  lint "],
                approvers: ["alice", "Alice"],
                now);
            var credential = RepositoryCredentialRef.Create(candidate.Id, "integration-1", RepositoryAccess.Write, now);

            await store.GetOrRegisterAsync(candidate, policy, credential, cancellationToken);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RepositoriesDbContext>();
            var stored = await db.RepositoryPolicies.AsNoTracking().SingleAsync(cancellationToken);

            stored.ProtectedBranches.ShouldBe(["develop", "main"]);
            stored.RequiredChecks.ShouldBe(["lint"]);
            stored.Approvers.ShouldBe(["alice"]);
        }
    }

    [Fact(DisplayName = "Given a registered repository, when the credential's DefaultAccess is read back, then the wire form round-trips")]
    public async Task RoundTripCredentialDefaultAccessAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;

        await using (var scope = provider.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IRepositoryStore>();
            var candidate = Repository.Create("https://github.com/acme/app.git", "github.com", "main", now);
            var policy = RepositoryPolicy.Create(candidate.Id, [], [], [], now);
            var credential = RepositoryCredentialRef.Create(candidate.Id, "integration-write", RepositoryAccess.Write, now);

            await store.GetOrRegisterAsync(candidate, policy, credential, cancellationToken);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RepositoriesDbContext>();
            var stored = await db.RepositoryCredentialRefs.AsNoTracking().SingleAsync(cancellationToken);

            stored.DefaultAccess.ShouldBe(RepositoryAccess.Write);
            stored.IntegrationRef.ShouldBe("integration-write");
        }
    }

    [Fact(DisplayName = "Given a registered repository, when the repository is deleted, then the policy and credential cascade with it")]
    public async Task CascadeDeletePolicyAndCredentialWithRepositoryAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;

        RepositoryId repositoryId;
        await using (var scope = provider.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IRepositoryStore>();
            var candidate = Repository.Create("https://github.com/acme/app.git", "github.com", "main", now);
            repositoryId = candidate.Id;
            var policy = RepositoryPolicy.Create(candidate.Id, ["main"], ["lint"], ["alice"], now);
            var credential = RepositoryCredentialRef.Create(candidate.Id, "integration-1", RepositoryAccess.Write, now);

            await store.GetOrRegisterAsync(candidate, policy, credential, cancellationToken);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RepositoriesDbContext>();
            await db.Repositories.Where(repository => repository.Id == repositoryId).ExecuteDeleteAsync(cancellationToken);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RepositoriesDbContext>();
            var policies = await db.RepositoryPolicies.AsNoTracking().ToListAsync(cancellationToken);
            var credentials = await db.RepositoryCredentialRefs.AsNoTracking().ToListAsync(cancellationToken);

            policies.ShouldBeEmpty();
            credentials.ShouldBeEmpty();
        }
    }

    private async Task<List<string>> QuerySingleColumnAsync(string sql)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RepositoriesDbContext>();
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
        var db = scope.ServiceProvider.GetRequiredService<RepositoriesDbContext>();
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
        command.Parameters.Add(schemaParameter);
        var tableNameParameter = command.CreateParameter();
        tableNameParameter.ParameterName = "@tableName";
        tableNameParameter.Value = tableName;
        command.Parameters.Add(tableNameParameter);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns[reader.GetString(0)] = new ColumnSpec(reader.GetString(1), reader.GetString(2));
        }

        return columns;
    }

    private sealed record ColumnSpec(string DataType, string IsNullable);
}
