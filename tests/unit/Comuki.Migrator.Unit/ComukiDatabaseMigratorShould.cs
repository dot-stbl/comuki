using Comuki.Shared.Migrations;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace Comuki.Migrator.Unit;

/// <summary>
/// Boot-migration contract: <see cref="ComukiDatabaseMigrator.EnsureAllAsync"/>
/// migrates every module schema on a fresh database, is a no-op (empty
/// summary) on the second pass, and releases its advisory lock so a
/// immediately following pass does not block. The pgvector-dependent
/// migrations degrade gracefully on the plain postgres image, exactly as
/// they do in the integration harnesses that run the same ten contexts.
///
/// Every test skips when no Docker endpoint is reachable — the full unit
/// matrix runs on docker-less CI runners, and a container is this suite's
/// only real dependency (the same skip contract as the Vault integration
/// fixture).
/// </summary>
public sealed class ComukiDatabaseMigratorShould : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    /// <summary>
    /// Docker availability as observed at container start: Testcontainers
    /// surfaces a missing endpoint as <c>DockerUnavailableException</c>
    /// (sometimes wrapped in an <see cref="AggregateException"/>); any
    /// such failure means the suite skips, not fails.
    /// </summary>
    private bool ContainerStarted { get; set; }

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        try
        {
            await container.StartAsync(TestContext.Current.CancellationToken);
            ContainerStarted = true;
        }
        catch (Exception exception) when (exception.Message.Contains("Docker", StringComparison.Ordinal)
            || exception.InnerException?.Message.Contains("Docker", StringComparison.Ordinal) == true)
        {
            ContainerStarted = false;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (ContainerStarted)
        {
            await container.DisposeAsync();
        }
    }

    [Fact(DisplayName = "Given a fresh database, when EnsureAllAsync runs, then every module schema is migrated and the applied summary is non-empty")]
    public async Task MigratesEverySchemaOnFreshDatabaseAsync()
    {
        Assert.SkipUnless(ContainerStarted, "Migrator test requires Docker (Testcontainers); this runner has no Docker endpoint.");

        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = container.GetConnectionString();

        var summary = await ComukiDatabaseMigrator.EnsureAllAsync(connectionString, cancellationToken);

        summary.TotalApplied.ShouldBeGreaterThan(0);
        summary.AppliedSchemas.Select(static schema => schema.Schema).ShouldContain("memory");
        summary.AppliedSchemas.Select(static schema => schema.Schema).ShouldContain("orchestration");

        var schemas = await PostgresHelpers.QuerySchemasAsync(connectionString, cancellationToken);
        foreach (var schema in PostgresHelpers.AllSchemas())
        {
            schemas.ShouldContain(schema, $"{schema} should exist after EnsureAllAsync");
        }
    }

    [Fact(DisplayName = "Given an already-migrated database, when EnsureAllAsync runs again, then nothing is applied and the summary is empty")]
    public async Task SecondPassAppliesNothingAsync()
    {
        Assert.SkipUnless(ContainerStarted, "Migrator test requires Docker (Testcontainers); this runner has no Docker endpoint.");

        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = container.GetConnectionString();

        await ComukiDatabaseMigrator.EnsureAllAsync(connectionString, cancellationToken);
        var second = await ComukiDatabaseMigrator.EnsureAllAsync(connectionString, cancellationToken);

        second.TotalApplied.ShouldBe(0);
        second.AppliedSchemas.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given a leftover session advisory lock is impossible, when EnsureAllAsync completes, then pg_locks holds no comuki advisory lock for the releasing session")]
    public async Task ReleasesAdvisoryLockAfterCompletionAsync()
    {
        Assert.SkipUnless(ContainerStarted, "Migrator test requires Docker (Testcontainers); this runner has no Docker endpoint.");

        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = container.GetConnectionString();

        await ComukiDatabaseMigrator.EnsureAllAsync(connectionString, cancellationToken);

        // The lock connection is closed by EnsureAllAsync; a fresh session
        // must not observe any remaining advisory lock granted to it (a
        // leaked lock would hang every later boot replica on this key).
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_try_advisory_lock(6349762201409117)";
        var acquired = await command.ExecuteScalarAsync(cancellationToken);
        acquired.ShouldNotBeNull();
        acquired.ShouldBe(true, "the advisory lock key must be free after EnsureAllAsync completes");
    }
}
