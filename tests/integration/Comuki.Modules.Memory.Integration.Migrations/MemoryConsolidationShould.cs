using Comuki.Modules.Memory.Application.Ports;
using Comuki.Modules.Memory.Domain.Facts.Kinds;
using Comuki.Modules.Memory.Domain.Facts.Scopes;
using Comuki.Modules.Memory.Domain.Facts.Sources;
using Comuki.Modules.Memory.Infrastructure;
using Comuki.Modules.Memory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace Comuki.Modules.Memory.Integration.Migrations;

/// <summary>
/// The consolidation sleep cycle against a real Postgres: the access
/// tracking the promote pass feeds on (searches stamp
/// <c>read_count</c>/<c>last_read_at</c>), the promote UPDATE an
/// ephemeral fact with enough reads survives, the decay UPDATE a
/// long-unread standing fact is demoted with one day of grace — and the
/// regular sweep finishing the job once that grace elapses. Uses the
/// system-view store composition (no scope accessor), like the migrations
/// fixture.
/// </summary>
public sealed class MemoryConsolidationShould : IAsyncLifetime
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
        services.AddSingleton(TimeProvider.System);
        services.AddMemoryPersistence(container.GetConnectionString());
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

    [Fact(DisplayName = "Given a searched fact, when SearchAsync returns it, then read_count and last_read_at are stamped in Postgres")]
    public async Task SearchRegistersReadsAsync()
    {
        var store = Store;
        var cancellationToken = TestContext.Current.CancellationToken;
        await store.WriteAsync(Write("tracked", "searched fact"), cancellationToken);

        await SearchSubjectAsync(store, "user-1", cancellationToken);
        await SearchSubjectAsync(store, "user-1", cancellationToken);

        var tracked = await QuerySingleColumnAsync(
            $"SELECT read_count::text || '|' || EXTRACT(EPOCH FROM last_read_at)::bigint::text "
            + $"FROM {MemoryDatabase.Schema}.memory_facts WHERE topic_key = 'tracked'");
        var parts = tracked.ShouldHaveSingleItem().Split('|');
        parts[0].ShouldBe("2");
        parts[1].ShouldNotBeNullOrWhiteSpace();
    }

    [Fact(DisplayName = "Given an ephemeral fact read three times and an unread one, when the promote pass runs, then only the read one becomes standing")]
    public async Task PromoteReadEphemeralFactsAsync()
    {
        var store = Store;
        var cancellationToken = TestContext.Current.CancellationToken;
        await store.WriteAsync(Write("useful", "read three times", subjectId: "user-1", kind: MemoryFactKind.Ephemeral), cancellationToken);
        await store.WriteAsync(Write("ignored", "never read", subjectId: "user-2", kind: MemoryFactKind.Ephemeral), cancellationToken);
        // the promote pass demands an hour of age — backdate both rows
        await ExecuteNonQueryAsync(
            $"UPDATE {MemoryDatabase.Schema}.memory_facts SET created_at = created_at - interval '2 hours' "
            + "WHERE topic_key IN ('useful', 'ignored')",
            cancellationToken);
        for (var search = 0; search < 3; search++)
        {
            await SearchSubjectAsync(store, "user-1", cancellationToken);
        }

        var promoted = await store.PromoteReadFactsAsync(
            TimeProvider.System.GetUtcNow(), 3, TimeSpan.FromHours(1), cancellationToken);

        promoted.ShouldBe(1);
        var kinds = await QuerySingleColumnAsync(
            $"SELECT topic_key || '=' || kind FROM {MemoryDatabase.Schema}.memory_facts ORDER BY topic_key");
        kinds.ShouldBe(["ignored=ephemeral", "useful=standing"]);
    }

    [Fact(DisplayName = "Given standing facts unread for 90+ days and fresh ones, when the decay pass runs, then only the forgotten ones demote with one day of grace")]
    public async Task DecayUnreadStandingFactsAsync()
    {
        var store = Store;
        var cancellationToken = TestContext.Current.CancellationToken;
        await store.WriteAsync(Write("stale-read", "read long ago"), cancellationToken);
        await store.WriteAsync(Write("ancient", "never read since creation"), cancellationToken);
        await store.WriteAsync(Write("fresh", "read yesterday"), cancellationToken);
        await ExecuteNonQueryAsync(
            $"UPDATE {MemoryDatabase.Schema}.memory_facts SET last_read_at = now() - interval '91 days' WHERE topic_key = 'stale-read'",
            cancellationToken);
        await ExecuteNonQueryAsync(
            $"UPDATE {MemoryDatabase.Schema}.memory_facts SET created_at = now() - interval '120 days' WHERE topic_key = 'ancient'",
            cancellationToken);
        await ExecuteNonQueryAsync(
            $"UPDATE {MemoryDatabase.Schema}.memory_facts SET last_read_at = now() - interval '1 day' WHERE topic_key = 'fresh'",
            cancellationToken);

        var decayed = await store.DecayUnreadFactsAsync(
            TimeProvider.System.GetUtcNow(), TimeSpan.FromDays(90), cancellationToken);

        decayed.ShouldBe(2);
        var kinds = await QuerySingleColumnAsync(
            $"SELECT topic_key || '=' || kind FROM {MemoryDatabase.Schema}.memory_facts ORDER BY topic_key");
        kinds.ShouldBe(["ancient=ephemeral", "fresh=standing", "stale-read=ephemeral"]);

        // the grace window: decayed rows are 13 days old under a 14-day
        // horizon — the sweep must NOT reap them yet
        var sweptNow = await store.SweepExpiredAsync(TimeProvider.System.GetUtcNow(), cancellationToken);
        sweptNow.ShouldBe(0);

        // once the grace elapses the regular sweep finishes the cycle
        await ExecuteNonQueryAsync(
            $"UPDATE {MemoryDatabase.Schema}.memory_facts SET created_at = now() - interval '15 days' "
            + "WHERE topic_key IN ('ancient', 'stale-read')",
            cancellationToken);
        var sweptAfterGrace = await store.SweepExpiredAsync(TimeProvider.System.GetUtcNow(), cancellationToken);
        sweptAfterGrace.ShouldBe(2);
        var remaining = await store.ListAsync(MemoryScope.User, "user-1", cancellationToken: cancellationToken);
        remaining.ShouldHaveSingleItem().TopicKey.ShouldBe("fresh");
    }

    [Fact(DisplayName = "Given no eligible facts, when the consolidation passes run, then both report zero and counting answers the active total")]
    public async Task ReportZeroWhenNothingToConsolidateAsync()
    {
        var store = Store;
        var cancellationToken = TestContext.Current.CancellationToken;
        await store.WriteAsync(Write("just-standing", "fresh standing"), cancellationToken);

        var promoted = await store.PromoteReadFactsAsync(
            TimeProvider.System.GetUtcNow(), 3, TimeSpan.FromHours(1), cancellationToken);
        var decayed = await store.DecayUnreadFactsAsync(
            TimeProvider.System.GetUtcNow(), TimeSpan.FromDays(90), cancellationToken);
        var total = await store.CountActiveFactsAsync(cancellationToken);

        promoted.ShouldBe(0);
        decayed.ShouldBe(0);
        total.ShouldBe(1);
    }

    private IMemoryStore Store => provider.GetRequiredService<IMemoryStore>();

    private static Task SearchSubjectAsync(IMemoryStore store, string subjectId, CancellationToken cancellationToken)
    {
        return store.SearchAsync(
            new MemoryFactQuery(Scope: MemoryScope.User, SubjectId: subjectId),
            cancellationToken);
    }

    private static MemoryFactWrite Write(
        string topicKey,
        string text,
        string subjectId = "user-1",
        MemoryFactKind kind = MemoryFactKind.Standing)
    {
        return new MemoryFactWrite(
            MemoryScope.User,
            subjectId,
            kind,
            topicKey,
            text,
            MemorySource.Chat,
            subjectId);
    }

    private async Task ExecuteNonQueryAsync(string sql, CancellationToken cancellationToken)
    {
        var db = provider.GetRequiredService<MemoryDbContext>();
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<List<string>> QuerySingleColumnAsync(string sql)
    {
        var db = provider.GetRequiredService<MemoryDbContext>();
        await db.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var connection = db.Database.GetDbConnection();
        var rows = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }
}
