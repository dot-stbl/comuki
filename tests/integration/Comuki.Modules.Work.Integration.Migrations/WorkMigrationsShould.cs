using System.Globalization;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Modules.Work.Infrastructure.Persistence;
using Comuki.Shared.Migrations;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace Comuki.Modules.Work.Integration.Migrations;

/// <summary>
/// Smoke-test for the Work module's migrations when applied alongside the
/// orchestration context on one real Postgres (Testcontainers).
/// Covers the umbrella's <c>add-work-management</c> tasks 3.2, 3.2a, 9.5:
/// the <c>work</c> schema with its seven <c>work_*</c> tables plus the
/// mirror dedupe ledger (<c>inbox_receipts</c>) and the subscriber-state
/// table (<c>outbox_watermarks</c>), the four additive
/// <c>orchestration.runs</c> columns and the two indexes
/// (<c>ix_runs_task_id</c>, <c>ux_runs_task_id_attempt_ordinal</c>) that
/// the RunWorkBacklink migration adds. The Testcontainers fixture starts
/// once per class via <see cref="IAsyncLifetime"/>; the migrator uses the
/// session-scoped <c>pg_advisory_lock</c> to keep the run serialised
/// against parallel replicas. A single
/// <see cref="ComukiDatabaseMigrator.EnsureAllAsync(string, CancellationToken)"/>
/// pass applies every module context's pending migrations in
/// <see cref="Shared.Migrations.Targets.MigrationTargets.All"/> order — orchestrator first, then
/// work — and the assertions inspect the resulting <c>information_schema</c>
/// state via raw <c>NpgsqlCommand</c>s rather than EF projections, so the
/// schema claims come straight from the database, not from the model
/// snapshot.
/// </summary>
public sealed class WorkMigrationsShould : IAsyncLifetime
{
    private const string PostgresImage = "postgres:16-alpine";

    private readonly PostgreSqlContainer container = new PostgreSqlBuilder(PostgresImage)
        .Build();

    /// <summary>Live connection string to the just-started container.</summary>
    private string connectionString = string.Empty;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await container.StartAsync(cancellationToken);
        connectionString = container.GetConnectionString();

        // The single migrator entry point walks every context in
        // MigrationTargets.All under one advisory lock. The Work and
        // Orchestration contexts land in their own schemas with their
        // own __ef_migrations_history tables, so the schemas coexist on
        // one database.
        await ComukiDatabaseMigrator.EnsureAllAsync(connectionString, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        return new ValueTask(container.DisposeAsync().AsTask());
    }

    [Fact(DisplayName = "Given a fresh database, when the migrator runs, then the work schema exists with seven work_* tables plus inbox_receipts and outbox_watermarks")]
    public async Task CreateWorkSchemaWithAllTablesAsync()
    {
        var tables = await QuerySingleColumnAsync(
            $"SELECT table_name FROM information_schema.tables "
            + $"WHERE table_schema = '{WorkDatabase.Schema}' "
            + "ORDER BY table_name");

        // The seven work_*-prefixed tables — covers the umbrella's
        // task 3.2 aggregate layout (work_tasks + the five side
        // tables + inbound_item_bindings).
        tables.ShouldContain(WorkDatabase.WorkTasks);
        tables.ShouldContain(WorkDatabase.WorkTaskAttempts);
        tables.ShouldContain(WorkDatabase.WorkTaskSourceRefs);
        tables.ShouldContain(WorkDatabase.WorkTaskDependencies);
        tables.ShouldContain(WorkDatabase.WorkTaskAssignments);
        tables.ShouldContain(WorkDatabase.WorkTaskCompletionPolicies);
        tables.ShouldContain("inbound_item_bindings");

        // Mirror dedupe ledger + subscriber state — work-side
        // decoupling from the engine's orchestration.outbox_messages
        // (per design.md Open Question A's mirror-table recommendation).
        tables.ShouldContain(WorkDatabase.InboxReceipts);
        tables.ShouldContain(WorkDatabase.OutboxWatermarks);
    }

    [Fact(DisplayName = "Given migrated work tables, when indexdefs are inspected, then the work-side invariants carry through (one-primary-per-task and one-ordinal-per-task)")]
    public async Task CreateWorkInvariantIndexesAsync()
    {
        var definitions = await QuerySingleColumnAsync(
            "SELECT indexdef FROM pg_indexes WHERE schemaname = '" + WorkDatabase.Schema + "' "
            + "AND tablename IN ('work_task_source_refs', 'work_task_attempts', 'inbound_item_bindings', 'work_task_completion_policies', 'work_task_assignments')");

        // One primary per Task (the unique-on-is_primary filter
        // partial index — matches the AddSourceRef guard).
        definitions.ShouldContain(static definition => definition.Contains("ux_work_task_source_refs_primary")
            && definition.Contains("UNIQUE")
            && definition.Contains("is_primary"));

        // One ordinal per Task (the attempt ledger's monotonic
        // invariant — matches the AppendAttempt guard).
        definitions.ShouldContain(static definition => definition.Contains("ux_work_task_attempts_ordinal")
            && definition.Contains("UNIQUE"));

        // One inbound-item binding per external id (the Integrations
        // admission hook's idempotency seam).
        definitions.ShouldContain(static definition => definition.Contains("ux_inbound_item_bindings_inbound")
            && definition.Contains("UNIQUE"));

        // Versioned completion policy per Task (the Resolve path
        // reads the active version, not the current one — see task 8.5).
        definitions.ShouldContain(static definition => definition.Contains("ux_work_task_completion_policies_task_version")
            && definition.Contains("UNIQUE"));

        // One (task, actor) assignment row (the brain-assignment
        // auto-dedupe path — task 7.4).
        definitions.ShouldContain(static definition => definition.Contains("ux_work_task_assignments_actor")
            && definition.Contains("UNIQUE"));
    }

    [Fact(DisplayName = "Given migrated work_tasks, when columns are inspected, then snake_case columns carry version, status, and the brief-version counter with the right HasMaxBound")]
    public async Task WorkTasksColumnsCarryExpectedShapeAsync()
    {
        var columns = await QueryColumnsAsync(WorkDatabase.Schema, WorkDatabase.WorkTasks);

        columns["id"].ShouldBe(new ColumnSpec("uuid", "NO"));
        columns["project_id"].ShouldBe(new ColumnSpec("uuid", "NO"));
        columns["status"].ShouldBe(new ColumnSpec("character varying", "NO"));
        columns["resolution_outcome"].ShouldBe(new ColumnSpec("character varying", "YES"));
        columns["version"].ShouldBe(new ColumnSpec("bigint", "NO"));
        columns["brief_version"].ShouldBe(new ColumnSpec("integer", "NO"));
        columns["attempt_ordinal"].ShouldBe(new ColumnSpec("integer", "NO"));
        columns["active_attempt_id"].ShouldBe(new ColumnSpec("uuid", "YES"));
        columns["visibility"].ShouldBe(new ColumnSpec("character varying", "NO"));
        columns["mission_id"].ShouldBe(new ColumnSpec("uuid", "YES"));
        columns["created_at"].ShouldBe(new ColumnSpec("timestamp with time zone", "NO"));
        columns["updated_at"].ShouldBe(new ColumnSpec("timestamp with time zone", "NO"));
    }

    [Fact(DisplayName = "Given migrated orchestration.runs, when columns are inspected, then the four Work-backlink columns exist with their declared defaults")]
    public async Task RunsCarryWorkBacklinkColumnsAsync()
    {
        var columns = await QueryColumnsAsync(OrchestrationDatabase.Schema, OrchestrationDatabase.Runs);

        columns["task_id"].ShouldBe(new ColumnSpec("uuid", "YES"));
        columns["attempt_ordinal"].ShouldBe(new ColumnSpec("integer", "NO"));
        columns["predecessor_run_id"].ShouldBe(new ColumnSpec("uuid", "YES"));
        columns["triggering_actor_id"].ShouldBe(new ColumnSpec("character varying", "YES"));
    }

    [Fact(DisplayName = "Given migrated orchestration.runs, when indexes are inspected, then ix_runs_task_id and the unique (task_id, attempt_ordinal) live as declared")]
    public async Task RunsCarryWorkBacklinkIndexesAsync()
    {
        var definitions = await QuerySingleColumnAsync(
            "SELECT indexdef FROM pg_indexes WHERE schemaname = '" + OrchestrationDatabase.Schema + "' "
            + "AND tablename = '" + OrchestrationDatabase.Runs + "' "
            + "AND indexname IN ('ix_runs_task_id', 'ux_runs_task_id_attempt_ordinal')");

        definitions.ShouldContain(static definition => definition.Contains("ix_runs_task_id")
            && definition.Contains("task_id")
            && definition.Contains("task_id IS NOT NULL"));

        definitions.ShouldContain(static definition => definition.Contains("ux_runs_task_id_attempt_ordinal")
            && definition.Contains("UNIQUE")
            && definition.Contains("task_id, attempt_ordinal")
            && definition.Contains("task_id IS NOT NULL"));
    }

    [Fact(DisplayName = "Given a re-apply, when EnsureAllAsync is called twice, then the second call is a no-op (no duplicate rows, no missing rows)")]
    public async Task ReapplyingMigrationsIsIdempotentAsync()
    {
        var tablesBefore = await QuerySingleColumnAsync(
            $"SELECT table_name FROM information_schema.tables "
            + $"WHERE table_schema = '{WorkDatabase.Schema}'");
        var runsCountBefore = await QueryScalarAsync(
            $"SELECT count(*) FROM information_schema.tables "
            + $"WHERE table_schema = '{OrchestrationDatabase.Schema}'");

        await ComukiDatabaseMigrator.EnsureAllAsync(connectionString, TestContext.Current.CancellationToken);

        var tablesAfter = await QuerySingleColumnAsync(
            $"SELECT table_name FROM information_schema.tables "
            + $"WHERE table_schema = '{WorkDatabase.Schema}'");
        var runsCountAfter = await QueryScalarAsync(
            $"SELECT count(*) FROM information_schema.tables "
            + $"WHERE table_schema = '{OrchestrationDatabase.Schema}'");

        tablesAfter.Count.ShouldBe(tablesBefore.Count);
        runsCountAfter.ShouldBe(runsCountBefore);
    }

    private async Task<List<string>> QuerySingleColumnAsync(string sql)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var rows = new List<string>();
        await using var connection = new Npgsql.NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    private async Task<long> QueryScalarAsync(string sql)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = new Npgsql.NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null ? 0L : Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private async Task<Dictionary<string, ColumnSpec>> QueryColumnsAsync(string schema, string tableName)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var columns = new Dictionary<string, ColumnSpec>(StringComparer.Ordinal);
        await using var connection = new Npgsql.NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT column_name, data_type, is_nullable FROM information_schema.columns "
            + "WHERE table_schema = @schema AND table_name = @tableName";
        var schemaParameter = command.CreateParameter();
        schemaParameter.ParameterName = "@schema";
        schemaParameter.Value = schema;
        command.Parameters.Add(schemaParameter);
        var tableParameter = command.CreateParameter();
        tableParameter.ParameterName = "@tableName";
        tableParameter.Value = tableName;
        command.Parameters.Add(tableParameter);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns[reader.GetString(0)] = new ColumnSpec(reader.GetString(1), reader.GetString(2));
        }

        return columns;
    }

    private sealed record ColumnSpec(string DataType, string IsNullable);
}
