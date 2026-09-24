using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Modules.Verify.Application;
using Comuki.Modules.Verify.Application.Ports;
using Comuki.Modules.Verify.Domain.Runs;
using Comuki.Modules.Verify.Infrastructure;
using Comuki.Modules.Verify.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace Comuki.Modules.Verify.Integration.Migrations;

/// <summary>
/// Proves the Verify EF migration (applied alongside the orchestration
/// context on one real Postgres) creates the expected schema — the
/// <c>verify.generic_command_runs</c> table, its own migrations history,
/// the executable/arguments/status column shapes — and that
/// <see cref="IGenericCommandStore.ClaimPendingAsync"/>'s
/// <c>FOR UPDATE SKIP LOCKED</c> clause is valid Postgres SQL that only
/// ever claims Pending rows.
/// </summary>
public sealed class VerifyMigrationsShould : IAsyncLifetime
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

        // The migrator's contract: every module context migrates the same
        // database, each with its own migrations history table.
        var orchestrationOptions = new DbContextOptionsBuilder<OrchestrationDbContext>();
        OrchestrationDbContext.ApplyOptions(orchestrationOptions, connectionString);
        await using var orchestrationDb = new OrchestrationDbContext(orchestrationOptions.Options);
        await orchestrationDb.Database.MigrateAsync(cancellationToken);

        var services = new ServiceCollection();
        services.AddVerifyApplication();
        services.AddVerifyPersistence(connectionString);
        provider = services.BuildServiceProvider();

        var db = provider.GetRequiredService<VerifyDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await provider.DisposeAsync();
        await container.DisposeAsync();
    }

    [Fact(DisplayName = "Given an empty database, when both contexts migrate it, then the verify table and orchestration tables coexist with separate histories")]
    public async Task CreateVerifyTableAlongsideOrchestrationAsync()
    {
        var tables = await QuerySingleColumnAsync(
            $"SELECT table_name FROM information_schema.tables "
            + $"WHERE table_schema IN ('{VerifyDatabase.Schema}', '{OrchestrationDatabase.Schema}') "
            + "ORDER BY table_name");

        tables.ShouldContain(VerifyDatabase.GenericCommandRuns);
        tables.ShouldContain(OrchestrationDatabase.Runs);
        tables.ShouldContain("__ef_migrations_history");
    }

    [Fact(DisplayName = "Given the migrated table, when columns are inspected, then executable is bounded text and arguments is jsonb")]
    public async Task StoreExpectedColumnTypesAsync()
    {
        var columns = await QueryColumnsAsync(VerifyDatabase.Schema, VerifyDatabase.GenericCommandRuns);

        columns["executable"].ShouldBe(new ColumnSpec("character varying", "NO"));
        columns["arguments"].ShouldBe(new ColumnSpec("jsonb", "NO"));
        columns["status"].ShouldBe(new ColumnSpec("character varying", "NO"));
        columns["project_id"].ShouldBe(new ColumnSpec("uuid", "YES"));
        columns["output_log"].ShouldBe(new ColumnSpec("text", "NO"));
    }

    [Fact(DisplayName = "Given the migrated table, when indexes are inspected, then the status and project indexes exist")]
    public async Task CreateExpectedIndexesAsync()
    {
        var definitions = await QuerySingleColumnAsync(
            "SELECT indexname FROM pg_indexes WHERE schemaname = '" + VerifyDatabase.Schema + "' "
            + "AND tablename = '" + VerifyDatabase.GenericCommandRuns + "'");

        definitions.ShouldContain("ix_generic_command_runs_status");
        definitions.ShouldContain("ix_generic_command_runs_project");
    }

    [Fact(DisplayName = "Given one pending and one already-running row, when ClaimPendingAsync runs, then only the pending row is claimed")]
    public async Task ClaimOnlyPendingRowsAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = provider.GetRequiredService<IGenericCommandStore>();
        var now = DateTimeOffset.UtcNow;

        var pending = GenericCommandRun.Create(null, "general", "dotnet", ["--version"], 0, now);
        var alreadyRunning = GenericCommandRun.Create(null, "general", "dotnet", ["build"], 0, now.AddSeconds(1));
        alreadyRunning.MarkRunning(now.AddSeconds(2));

        await store.AddAsync(pending, cancellationToken);
        await store.AddAsync(alreadyRunning, cancellationToken);

        var claimed = await store.ClaimPendingAsync(limit: 10, cancellationToken);

        claimed.Count.ShouldBe(1);
        claimed[0].Id.ShouldBe(pending.Id);
        claimed[0].Arguments.ShouldBe(["--version"]);
    }

    private async Task<List<string>> QuerySingleColumnAsync(string sql)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VerifyDbContext>();
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
        var db = scope.ServiceProvider.GetRequiredService<VerifyDbContext>();
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
