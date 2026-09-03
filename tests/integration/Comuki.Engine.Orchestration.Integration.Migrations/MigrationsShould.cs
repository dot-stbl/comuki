using System.Text.Json;
using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Journal;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Domain.WorkItems;
using Comuki.Engine.Orchestration.Infrastructure;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace Comuki.Engine.Orchestration.Integration.Migrations;

/// <summary>
/// Proves the EF migrations (authored for <see cref="OrchestrationDbContext"/>,
/// applied by Comuki.Migrator) create the expected schema on a real Postgres:
/// tables, claim-path indexes (incl. the partial live-status index), uuid/jsonb
/// column types, and a full aggregate round-trip through the migrated model.
/// </summary>
public sealed class MigrationsShould : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    /// <summary>
    /// boundary: initialised in InitializeAsync before any test runs
    /// </summary>
    private ServiceProvider provider = null!;
    /// <summary>
    /// boundary: initialised in InitializeAsync before any test runs
    /// </summary>
    private OrchestrationDbContext db = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        await container.StartAsync(TestContext.Current.CancellationToken);

        var services = new ServiceCollection();
        _ = services.AddOrchestrationPersistence(container.GetConnectionString());
        provider = services.BuildServiceProvider();
        db = provider.GetRequiredService<OrchestrationDbContext>();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        await db.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await provider.DisposeAsync();
        await container.DisposeAsync();
    }

    [Fact(DisplayName = "Given an empty database, when migrations are applied, then all orchestration tables exist")]
    public async Task CreateOrchestrationTablesAsync()
    {
        var tables = await QuerySingleColumnAsync(
            $"SELECT table_name FROM information_schema.tables WHERE table_schema = '{OrchestrationDatabase.Schema}' ORDER BY table_name");

        tables.ShouldContain(OrchestrationDatabase.Runs);
        tables.ShouldContain(OrchestrationDatabase.WorkItems);
        tables.ShouldContain(OrchestrationDatabase.WorkItemDependencies);
        tables.ShouldContain(OrchestrationDatabase.RunEvents);
    }

    [Fact(DisplayName = "Given migrated work_items, when indexes are inspected, then claim indexes exist")]
    public async Task CreateClaimIndexesOnWorkItemsAsync()
    {
        var definitions = await QuerySingleColumnAsync(
            $"SELECT indexdef FROM pg_indexes WHERE schemaname = '{OrchestrationDatabase.Schema}' AND tablename = '{OrchestrationDatabase.WorkItems}'");

        definitions.ShouldContain(static index => index.Contains("ix_work_items_run_id"));
        // pg normalises the predicate with ::text casts — assert on the essentials.
        // EF stores PascalCase enum names, so the partial filters must match them.
        definitions.ShouldContain(static index =>
            index.Contains("ix_work_items_active")
            && index.Contains("'Queued'")
            && index.Contains("'Running'"));
        definitions.ShouldContain(static index =>
            index.Contains("ix_work_items_claim")
            && index.Contains("profile_key")
            && index.Contains("'Queued'"));
    }

    [Fact(DisplayName = "Given migrated run_events, when indexes are inspected, then the timeline index exists")]
    public async Task CreateJournalTimelineIndexAsync()
    {
        var definitions = await QuerySingleColumnAsync(
            $"SELECT indexdef FROM pg_indexes WHERE schemaname = '{OrchestrationDatabase.Schema}' AND tablename = '{OrchestrationDatabase.RunEvents}'");

        var timeline = definitions.Single(static index => index.Contains("ix_run_events_run_id_occurred_at", StringComparison.Ordinal));
        timeline.ShouldContain("run_id");
        timeline.ShouldContain("occurred_at");
    }

    [Fact(DisplayName = "Given migrated columns, when types are inspected, then ids are uuid and payloads are jsonb")]
    public async Task StoreUuidAndJsonbColumnsAsync()
    {
        var workItemColumns = await QueryColumnsAsync(OrchestrationDatabase.Schema, OrchestrationDatabase.WorkItems);

        workItemColumns["id"].ShouldBe(new ColumnSpec("uuid", "NO"));
        workItemColumns["leased_by"].ShouldBe(new ColumnSpec("uuid", "YES"));
        workItemColumns["brief"].ShouldBe(new ColumnSpec("jsonb", "NO"));
        workItemColumns["status"].ShouldBe(new ColumnSpec("character varying", "NO"));
        workItemColumns["image"].ShouldBe(new ColumnSpec("character varying", "NO"));
        workItemColumns["profiles_ref"].ShouldBe(new ColumnSpec("character varying", "NO"));
        workItemColumns["attempt"].ShouldBe(new ColumnSpec("integer", "NO"));

        var runColumns = await QueryColumnsAsync(OrchestrationDatabase.Schema, OrchestrationDatabase.Runs);
        runColumns["id"].ShouldBe(new ColumnSpec("uuid", "NO"));

        var runEventColumns = await QueryColumnsAsync(OrchestrationDatabase.Schema, OrchestrationDatabase.RunEvents);
        runEventColumns["payload"].ShouldBe(new ColumnSpec("jsonb", "NO"));
    }

    [Fact(DisplayName = "Given a run with items, dependencies and journal, when saved and re-read, then everything round-trips")]
    public async Task RoundtripRunAggregateAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var run = Run.Create(ProjectId.New(), now);
        run.TransitionTo(RunStatus.Waiting, now);
        run.TransitionTo(RunStatus.Running, now.AddMinutes(1));

        var prerequisite = WorkItem.Create(run.Id, "explore-readonly", "ghcr.io/comuki/worker@sha256:9f86d0", "refs/heads/main", /*lang=json,strict*/ """{"goal":"read the repo"}""", WorkItemStatus.Queued, now);
        var dependent = WorkItem.Create(run.Id, "implement", "ghcr.io/comuki/worker@sha256:9f86d0", "refs/heads/main", /*lang=json,strict*/ """{"goal":"write the fix"}""", WorkItemStatus.Blocked, now);
        var dependency = WorkItemDependency.Create(dependent.Id, prerequisite.Id);
        var runEvent = RunEvent.Create(
            run.Id,
            RunEventTypes.RunStatusChanged,
            $$"""{"from":"{{RunStatus.Queued}}","to":"{{RunStatus.Waiting}}"}""",
            now.AddSeconds(5));

        _ = db.Runs.Add(run);
        db.WorkItems.AddRange(prerequisite, dependent);
        _ = db.WorkItemDependencies.Add(dependency);
        _ = db.RunEvents.Add(runEvent);
        _ = await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();

        var readOptions = new DbContextOptionsBuilder<OrchestrationDbContext>();
        OrchestrationDbContext.ApplyOptions(readOptions, container.GetConnectionString());
        await using var readDb = new OrchestrationDbContext(readOptions.Options);

        var storedRun = (await readDb.Runs.ToListAsync(cancellationToken)).ShouldHaveSingleItem();
        storedRun.Id.ShouldBe(run.Id);
        storedRun.Status.ShouldBe(RunStatus.Running);

        var storedItems = await readDb.WorkItems.AsNoTracking().ToListAsync(cancellationToken);
        storedItems.Count.ShouldBe(2);
        storedItems.ShouldContain(item => item.Status == WorkItemStatus.Queued && item.ProfileKey == "explore-readonly");
        storedItems.ShouldContain(item => item.Status == WorkItemStatus.Blocked && item.RunId == run.Id);

        var storedEvent = (await readDb.RunEvents.ToListAsync(cancellationToken)).ShouldHaveSingleItem();
        storedEvent.RunId.ShouldBe(run.Id);
        storedEvent.Type.ShouldBe(RunEventTypes.RunStatusChanged);

        // jsonb normalises key order/whitespace — compare the parsed value, not the text
        using var payload = JsonDocument.Parse(storedEvent.Payload);
        payload.RootElement.GetProperty("from").GetString().ShouldBe(nameof(RunStatus.Queued));
        payload.RootElement.GetProperty("to").GetString().ShouldBe(nameof(RunStatus.Waiting));

        var storedDependency = (await readDb.WorkItemDependencies.AsNoTracking().ToListAsync(cancellationToken)).ShouldHaveSingleItem();
        storedDependency.WorkItemId.ShouldBe(dependent.Id);
        storedDependency.DependsOnWorkItemId.ShouldBe(prerequisite.Id);
    }

    private async Task<List<string>> QuerySingleColumnAsync(string sql)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
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
