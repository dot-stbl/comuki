using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Modules.Integrations.Application;
using Comuki.Modules.Integrations.Application.Ports.Tickets;
using Comuki.Modules.Integrations.Domain.Connections;
using Comuki.Modules.Integrations.Domain.Deliveries;
using Comuki.Modules.Integrations.Domain.Ids;
using Comuki.Modules.Integrations.Domain.Items;
using Comuki.Modules.Integrations.Domain.Sync;
using Comuki.Modules.Integrations.Infrastructure;
using Comuki.Modules.Integrations.Infrastructure.Persistence;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace Comuki.Modules.Integrations.Integration.Migrations;

/// <summary>
/// Proves the Integrations EF migrations (applied alongside the
/// orchestration context on one real Postgres) create the expected
/// schema — the five integrations tables, the module-private history,
/// both idempotency locks — and that the store translates unique
/// violations into the friendly contract: delivery replays, the
/// one-active-ticket lock with its release path, and the terminal-once
/// sync enqueue.
/// </summary>
public sealed class IntegrationsMigrationsShould : IAsyncLifetime
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
        services.AddIntegrationsPersistence(connectionString);
        services.AddIntegrationsApplication();
        provider = services.BuildServiceProvider();

        var db = provider.GetRequiredService<IntegrationsDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await provider.DisposeAsync();
        await container.DisposeAsync();
    }

    [Fact(DisplayName = "Given an empty database, when both contexts migrate it, then integrations tables and orchestration tables coexist with separate histories")]
    public async Task CreateIntegrationsTablesAlongsideOrchestrationAsync()
    {
        var tables = await QuerySingleColumnAsync(
            $"SELECT table_name FROM information_schema.tables "
            + $"WHERE table_schema IN ('{IntegrationsDatabase.Schema}', '{OrchestrationDatabase.Schema}') "
            + "ORDER BY table_name");

        tables.ShouldContain(IntegrationsDatabase.Tickets);
        tables.ShouldContain(IntegrationsDatabase.Deliveries);
        tables.ShouldContain(IntegrationsDatabase.Connections);
        tables.ShouldContain(IntegrationsDatabase.Rules);
        tables.ShouldContain(IntegrationsDatabase.SyncJobs);
        tables.ShouldContain(OrchestrationDatabase.Runs);
        tables.ShouldContain("n_history");
    }

    [Fact(DisplayName = "Given migrated integrations schema, when indexes are inspected, then both idempotency locks exist as declared")]
    public async Task CreateIdempotencyIndexesAsync()
    {
        var definitions = await QuerySingleColumnAsync(
            "SELECT indexdef FROM pg_indexes WHERE schemaname = '" + IntegrationsDatabase.Schema + "' "
            + "AND tablename IN ('inbound_items', 'deliveries', 'sync_jobs')");

        definitions.ShouldContain(static definition => definition.Contains("ux_deliveries_source_delivery")
            && definition.Contains("UNIQUE"));
        // pg normalizes the predicate with ::text casts — assert the
        // shape, not the literal text
        definitions.ShouldContain(static definition => definition.Contains("ux_inbound_items_active")
            && definition.Contains("UNIQUE")
            && definition.Contains("'Pending'")
            && definition.Contains("'Claimed'"));
        definitions.ShouldContain(static definition => definition.Contains("ux_sync_jobs_run_id")
            && definition.Contains("UNIQUE"));
    }

    [Fact(DisplayName = "Given a stored delivery, when the same letter arrives again, then the insert-first lock answers replay")]
    public async Task RefuseDuplicateDeliveryAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = provider.GetRequiredService<IIntegrationsStore>();
        var delivery = Delivery.Create(TicketProviderKeys.GitHub, "delivery-1", DateTimeOffset.UtcNow);

        (await store.TryInsertDeliveryAsync(delivery, cancellationToken)).ShouldBeTrue();
        (await store.TryInsertDeliveryAsync(
            Delivery.Create(TicketProviderKeys.GitHub, "delivery-1", DateTimeOffset.UtcNow),
            cancellationToken)).ShouldBeFalse();
    }

    [Fact(DisplayName = "Given an active ticket, when the same issue arrives again, then the one-live-run lock refuses it")]
    public async Task RefuseSecondActiveTicketAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = provider.GetRequiredService<IIntegrationsStore>();
        var projectId = ProjectId.New();
        var now = DateTimeOffset.UtcNow;

        var first = InboundItem.Create(projectId, TicketProvider.GitHub, "dot-stbl/comuki#1", "first", string.Empty, "a", "u", "dot-stbl/comuki", ["bug"], InboundItemKind.Issue, now);
        (await store.TryInsertTicketAsync(first, cancellationToken)).ShouldNotBeNull();

        var second = InboundItem.Create(projectId, TicketProvider.GitHub, "dot-stbl/comuki#1", "second", string.Empty, "a", "u", "dot-stbl/comuki", [], InboundItemKind.Issue, now);
        (await store.TryInsertTicketAsync(second, cancellationToken)).ShouldBeNull();

        // a different repo's issue with the same number does not collide —
        // the external id is fully qualified
        var otherRepo = InboundItem.Create(projectId, TicketProvider.GitHub, "dot-stbl/other#1", "other", string.Empty, "a", "u", "dot-stbl/other", [], InboundItemKind.Issue, now);
        (await store.TryInsertTicketAsync(otherRepo, cancellationToken)).ShouldNotBeNull();
    }

    [Fact(DisplayName = "Given a released ticket, when the issue arrives again, then a fresh active ticket is admissible")]
    public async Task ReadmitAfterReleaseAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = provider.GetRequiredService<IIntegrationsStore>();
        var projectId = ProjectId.New();
        var now = DateTimeOffset.UtcNow;
        var runId = RunId.New();

        var ticket = InboundItem.Create(projectId, TicketProvider.GitLab, "acme/app#7", "t", string.Empty, "a", "u", "acme/app", [], InboundItemKind.Issue, now);
        (await store.TryInsertTicketAsync(ticket, cancellationToken)).ShouldNotBeNull();
        (await store.TryMarkClaimedAsync(ticket.Id, runId, cancellationToken)).ShouldBeTrue();
        await store.ReleaseTicketAsync(ticket.Id, cancellationToken);

        var retry = InboundItem.Create(projectId, TicketProvider.GitLab, "acme/app#7", "t again", string.Empty, "a", "u", "acme/app", [], InboundItemKind.Issue, now);
        (await store.TryInsertTicketAsync(retry, cancellationToken)).ShouldNotBeNull();
    }

    [Fact(DisplayName = "Given a claimed ticket, when it is claimed again, then the guarded update refuses the second claim")]
    public async Task RefuseDoubleClaimAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = provider.GetRequiredService<IIntegrationsStore>();
        var now = DateTimeOffset.UtcNow;

        var ticket = InboundItem.Create(ProjectId.New(), TicketProvider.Jira, "COM-1", "t", string.Empty, "a", "u", "COM", [], InboundItemKind.Issue, now);
        (await store.TryInsertTicketAsync(ticket, cancellationToken)).ShouldNotBeNull();

        (await store.TryMarkClaimedAsync(ticket.Id, RunId.New(), cancellationToken)).ShouldBeTrue();
        (await store.TryMarkClaimedAsync(ticket.Id, RunId.New(), cancellationToken)).ShouldBeFalse();
    }

    [Fact(DisplayName = "Given an enqueued sync job, when the same run is enqueued again, then the terminal-once index keeps one row")]
    public async Task KeepOneSyncJobPerRunAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = provider.GetRequiredService<IIntegrationsStore>();
        var now = DateTimeOffset.UtcNow;
        var projectId = ProjectId.New();

        var connection = SourceConnection.Create(projectId, TicketProvider.GitHub, "c", "{}", "COMUKI_GH_HOOK", "key1234567890abcd", now);
        await store.AddConnectionAsync(connection, cancellationToken);

        var ticket = InboundItem.Create(projectId, TicketProvider.GitHub, "dot-stbl/comuki#9", "t", string.Empty, "a", "u", "dot-stbl/comuki", [], InboundItemKind.Issue, now);
        (await store.TryInsertTicketAsync(ticket, cancellationToken)).ShouldNotBeNull();
        ticket.BindConnection(connection.Id);

        var runId = RunId.New();
        await store.EnqueueSyncJobAsync(SyncJob.Create(ticket.Id, connection.Id, runId, ticket.ExternalId, ticket.Url, "Succeeded", now), cancellationToken);
        await store.EnqueueSyncJobAsync(SyncJob.Create(ticket.Id, connection.Id, runId, ticket.ExternalId, ticket.Url, "Succeeded", now), cancellationToken);

        var db = provider.GetRequiredService<IntegrationsDbContext>();
        (await db.SyncJobs.AsNoTracking().CountAsync(job => job.RunId == runId, cancellationToken)).ShouldBe(1);
    }

    [Fact(DisplayName = "Given a failed sync attempt, when the job is marked failed, then attempts grow and the backoff defers the next try")]
    public async Task ApplySyncBackoffAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = provider.GetRequiredService<IIntegrationsStore>();
        var now = DateTimeOffset.UtcNow;
        var projectId = ProjectId.New();

        var ticket = InboundItem.Create(projectId, TicketProvider.GitHub, "dot-stbl/comuki#11", "t", string.Empty, "a", "u", "dot-stbl/comuki", [], InboundItemKind.Issue, now);
        (await store.TryInsertTicketAsync(ticket, cancellationToken)).ShouldNotBeNull();

        var job = SyncJob.Create(ticket.Id, SourceConnectionId.New(), RunId.New(), ticket.ExternalId, ticket.Url, "Failed", now);
        await store.EnqueueSyncJobAsync(job, cancellationToken);

        await store.MarkSyncJobFailedAsync(job.Id, "boom", maxAttempts: 3, backoff: TimeSpan.FromSeconds(30), now, cancellationToken);

        var db = provider.GetRequiredService<IntegrationsDbContext>();
        var stored = await db.SyncJobs.AsNoTracking().SingleAsync(candidate => candidate.Id == job.Id, cancellationToken);
        stored.Attempts.ShouldBe(1);
        stored.Status.ShouldBe(SyncJobStatus.Pending);
        stored.NextAttemptAt.ShouldBe(now.AddSeconds(30), tolerance: TimeSpan.FromMilliseconds(5));

        (await store.ListDueSyncJobsAsync(now, 10, cancellationToken)).ShouldNotContain(candidate => candidate.Id == job.Id);
        (await store.ListDueSyncJobsAsync(now.AddSeconds(31), 10, cancellationToken)).ShouldContain(candidate => candidate.Id == job.Id);
    }

    [Fact(DisplayName = "Given migrated inbound_items, when columns are inspected, then labels are text[] and settings are jsonb")]
    public async Task StoreExpectedColumnTypesAsync()
    {
        var tickets = await QueryColumnsAsync(IntegrationsDatabase.Schema, IntegrationsDatabase.Tickets);
        tickets["labels"].ShouldBe(new ColumnSpec("ARRAY", "NO"));
        tickets["provider"].ShouldBe(new ColumnSpec("character varying", "NO"));
        tickets["connection_id"].ShouldBe(new ColumnSpec("uuid", "YES"));

        var connections = await QueryColumnsAsync(IntegrationsDatabase.Schema, IntegrationsDatabase.Connections);
        connections["settings_json"].ShouldBe(new ColumnSpec("jsonb", "NO"));
        connections["secret_env_ref"].ShouldBe(new ColumnSpec("character varying", "NO"));
    }

    private async Task<List<string>> QuerySingleColumnAsync(string sql)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IntegrationsDbContext>();
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
        var db = scope.ServiceProvider.GetRequiredService<IntegrationsDbContext>();
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
