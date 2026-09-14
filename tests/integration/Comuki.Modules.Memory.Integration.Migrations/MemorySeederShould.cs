using Comuki.Modules.Memory.Domain.Facts.Scopes;
using Comuki.Modules.Memory.Infrastructure.Persistence;
using Comuki.Modules.Memory.Infrastructure.Persistence.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace Comuki.Modules.Memory.Integration.Migrations;

/// <summary>
/// Boot-seeder contract: the first pass inserts the three standing
/// <c>platform.*</c> facts in the global scope, the second pass is a
/// no-op (identical text → unchanged), and a changed text (a version
/// bump) supersedes the previous row instead of duplicating it — the
/// store's normal supersede mechanism, driven by the seeder.
/// </summary>
public sealed class MemorySeederShould : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("pgvector/pgvector:pg16")
        .Build();

    /// <summary>boundary: initialised in InitializeAsync before any test runs</summary>
    private ServiceProvider provider = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await container.StartAsync(cancellationToken);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContextFactory<MemoryDbContext>(options =>
            MemoryDbContext.ApplyOptions(options, container.GetConnectionString()));
        provider = services.BuildServiceProvider();

        var db = provider.GetRequiredService<MemoryDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await provider.DisposeAsync();
        await container.DisposeAsync();
    }

    [Fact(DisplayName = "Given a migrated empty database, when SeedAsync runs, then three standing platform facts are written to the global scope")]
    public async Task SeedPlatformFactsAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var result = await MemorySeeder.SeedAsync(container.GetConnectionString(), "1.2.3", cancellationToken);

        result.Written.ShouldBe(3);
        result.Superseded.ShouldBe(0);
        result.Unchanged.ShouldBe(0);

        var rows = await QueryAsync(
            "SELECT topic_key, kind, source, created_by, text FROM memory.memory_facts WHERE superseded_at IS NULL ORDER BY topic_key",
            cancellationToken);

        rows.Select(static row => row[0]).ShouldBe(["platform.architecture", "platform.conventions", "platform.identity"]);
        foreach (var row in rows)
        {
            row[1].ShouldBe("standing");
            row[2].ShouldBe("seeder");
            row[3].ShouldBe("seeder");
        }

        rows.Single(static row => row[0] == "platform.identity")[4].ShouldContain("Version: 1.2.3");
    }

    [Fact(DisplayName = "Given an already-seeded database, when SeedAsync runs again with the same version, then nothing changes")]
    public async Task SecondPassIsANoOpAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = container.GetConnectionString();

        await MemorySeeder.SeedAsync(connectionString, "1.2.3", cancellationToken);
        var second = await MemorySeeder.SeedAsync(connectionString, "1.2.3", cancellationToken);

        second.Written.ShouldBe(0);
        second.Superseded.ShouldBe(0);
        second.Unchanged.ShouldBe(3);

        var active = await QuerySingleColumnAsync(
            "SELECT count(*)::text FROM memory.memory_facts WHERE superseded_at IS NULL",
            cancellationToken);
        active.ShouldHaveSingleItem().ShouldBe("3");
    }

    [Fact(DisplayName = "Given a seeded database, when SeedAsync runs with a new version, then the identity fact is superseded and history is kept")]
    public async Task VersionBumpSupersedesIdentityFactAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = container.GetConnectionString();

        await MemorySeeder.SeedAsync(connectionString, "1.2.3", cancellationToken);
        var upgraded = await MemorySeeder.SeedAsync(connectionString, "2.0.0", cancellationToken);

        upgraded.Written.ShouldBe(0);
        upgraded.Superseded.ShouldBe(1);
        upgraded.Unchanged.ShouldBe(2);

        var activeIdentity = await QuerySingleColumnAsync(
            "SELECT text FROM memory.memory_facts WHERE topic_key = 'platform.identity' AND superseded_at IS NULL",
            cancellationToken);
        activeIdentity.ShouldHaveSingleItem().ShouldContain("Version: 2.0.0");

        // the superseded row keeps the old version — history survives the upgrade
        var supersededIdentity = await QuerySingleColumnAsync(
            "SELECT text FROM memory.memory_facts WHERE topic_key = 'platform.identity' AND superseded_at IS NOT NULL",
            cancellationToken);
        supersededIdentity.ShouldHaveSingleItem().ShouldContain("Version: 1.2.3");
    }

    [Fact(DisplayName = "Given seeded platform facts, when the digest scope reads global facts, then the seeded rows are visible as global")]
    public async Task SeededFactsLiveInTheGlobalScopeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = container.GetConnectionString();

        await MemorySeeder.SeedAsync(connectionString, "1.2.3", cancellationToken);

        var scopeAndSubject = await QueryAsync(
            "SELECT DISTINCT scope, subject_id FROM memory.memory_facts WHERE superseded_at IS NULL",
            cancellationToken);

        scopeAndSubject.ShouldHaveSingleItem();
        scopeAndSubject[0][0].ShouldBe(MemoryScopeKeys.Key(MemoryScope.Global));
        scopeAndSubject[0][1].ShouldBe(MemoryScopeKeys.GlobalSubject);
    }

    private async Task<List<string[]>> QueryAsync(string sql, CancellationToken cancellationToken)
    {
        var rows = new List<string[]>();
        var factory = provider.GetRequiredService<IDbContextFactory<MemoryDbContext>>();
        await using var db = factory.CreateDbContext();
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var values = new string[reader.FieldCount];
            for (var index = 0; index < reader.FieldCount; index++)
            {
                values[index] = reader.GetValue(index).ToString() ?? string.Empty;
            }
            rows.Add(values);
        }

        return rows;
    }

    private async Task<List<string>> QuerySingleColumnAsync(string sql, CancellationToken cancellationToken)
    {
        var rows = await QueryAsync(sql, cancellationToken);
        return [.. rows.Select(static row => row[0])];
    }
}
