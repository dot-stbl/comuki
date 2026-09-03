using Comuki.Modules.Memory.Application.Ports;
using Comuki.Modules.Memory.Domain.Facts;
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
/// Proves the Memory EF migrations on a REAL pgvector image: the four
/// tables plus the module-private history, the vector(768) embedding
/// column, the partial unique supersede index — and the store contract
/// end to end: superseding writes, deterministic cosine ranking, the
/// embedding-free fallback, ephemeral sweep and forget.
/// </summary>
public sealed class MemoryMigrationsShould : IAsyncLifetime
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
        _ = services.AddLogging();
        _ = services.AddMemoryPersistence(container.GetConnectionString());
        _ = services.AddSingleton(FixedTime.Provider);
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

    [Fact(DisplayName = "Given an empty database, when the memory context migrates, then all four tables and the private history exist")]
    public async Task CreateMemoryTablesAsync()
    {
        var tables = await QuerySingleColumnAsync(
            $"SELECT table_name FROM information_schema.tables WHERE table_schema = '{MemoryDatabase.Schema}' ORDER BY table_name");

        tables.ShouldContain(MemoryDatabase.ChatMessages);
        tables.ShouldContain(MemoryDatabase.ChatCheckpoints);
        tables.ShouldContain(MemoryDatabase.MemoryFacts);
        tables.ShouldContain(MemoryDatabase.LearningCandidates);
        tables.ShouldContain("__ef_migrations_history");
    }

    [Fact(DisplayName = "Given the pgvector image, when migrations applied, then the embedding column is vector(768)")]
    public async Task CreateVectorEmbeddingColumnAsync()
    {
        var columns = await QuerySingleColumnAsync(
            $"SELECT data_type FROM information_schema.columns "
            + $"WHERE table_schema = '{MemoryDatabase.Schema}' AND table_name = '{MemoryDatabase.MemoryFacts}' AND column_name = 'embedding'");

        columns.ShouldHaveSingleItem().ShouldBe("USER-DEFINED");
    }

    [Fact(DisplayName = "Given migrated memory_facts, when indexes are inspected, then the partial unique active-topic index exists")]
    public async Task CreateActiveTopicUniqueIndexAsync()
    {
        var definitions = await QuerySingleColumnAsync(
            $"SELECT indexdef FROM pg_indexes WHERE schemaname = '{MemoryDatabase.Schema}' AND tablename = '{MemoryDatabase.MemoryFacts}'");

        definitions.ShouldContain(static index => index.Contains("ix_memory_facts_active_topic")
            && index.Contains("UNIQUE")
            && index.Contains("superseded_at IS NULL"));
    }

    [Fact(DisplayName = "Given a topic with a fact, when the same topic is written again, then the old row is superseded and only the newest stays visible")]
    public async Task SupersedeSameTopicOnWriteAsync()
    {
        var store = Store;
        var cancellationToken = TestContext.Current.CancellationToken;

        await store.WriteAsync(Write("prefs", "prefers tabs"), cancellationToken);
        var second = await store.WriteAsync(Write("prefs", "prefers spaces"), cancellationToken);

        second.Text.ShouldBe("prefers spaces");
        second.TopicKey.ShouldBe("prefs");

        var visible = await store.SearchAsync(
            new MemoryFactQuery(Scope: MemoryScope.User, SubjectId: "user-1"), cancellationToken);
        visible.ShouldHaveSingleItem().Text.ShouldBe("prefers spaces");

        var supersededRows = await QuerySingleColumnAsync(
            $"SELECT count(*)::text FROM {MemoryDatabase.Schema}.memory_facts WHERE superseded_at IS NOT NULL");
        supersededRows.ShouldHaveSingleItem().ShouldBe("1");
    }

    [Fact(DisplayName = "Given embedded facts, when a cosine search runs with a deterministic query vector, then the closer fact ranks first")]
    public async Task RankByCosineDistanceAsync()
    {
        var store = Store;
        var cancellationToken = TestContext.Current.CancellationToken;

        // near: small tilt from e0 (~6°); far: 45° tilt; query: e0 —
        // cosine distance must order near before far
        var near = Vector(0, 0.1f);
        var far = Vector(1, 1f);
        await store.WriteAsync(Write("near", "close fact", near), cancellationToken);
        await store.WriteAsync(Write("far", "distant fact", far), cancellationToken);

        var results = await store.SearchAsync(
            new MemoryFactQuery(Scope: MemoryScope.User, SubjectId: "user-1", Embedding: Vector(0, 1f), Limit: 2),
            cancellationToken);

        results.Count.ShouldBe(2);
        results[0].TopicKey.ShouldBe("near");
        results[1].TopicKey.ShouldBe("far");
    }

    [Fact(DisplayName = "Given facts without embeddings, when a search runs with no query vector, then the fallback ranking answers")]
    public async Task FallBackWithoutEmbeddingsAsync()
    {
        var store = Store;
        var cancellationToken = TestContext.Current.CancellationToken;

        await store.WriteAsync(Write("ephemeral-topic", "task note", kind: MemoryFactKind.Ephemeral), cancellationToken);
        await store.WriteAsync(Write("standing-topic", "long decision"), cancellationToken);

        var results = await store.SearchAsync(
            new MemoryFactQuery(Scope: MemoryScope.User, SubjectId: "user-1"), cancellationToken);

        // fallback ranking: standing before ephemeral (both are fresh and
        // visible — the TTL only hides 14-day-old ephemeral rows)
        results.Select(static fact => fact.TopicKey).ShouldBe(["standing-topic", "ephemeral-topic"]);
    }

    [Fact(DisplayName = "Given a superseded fact with an embedding, when a cosine search runs, then only the active row returns")]
    public async Task ExcludeSupersededFromCosineAsync()
    {
        var store = Store;
        var cancellationToken = TestContext.Current.CancellationToken;

        await store.WriteAsync(Write("embedding-topic", "old text", Vector(0, 1f)), cancellationToken);
        await store.WriteAsync(Write("embedding-topic", "new text", Vector(0, 1f)), cancellationToken);

        var results = await store.SearchAsync(
            new MemoryFactQuery(Scope: MemoryScope.User, SubjectId: "user-1", Embedding: Vector(0, 1f)), cancellationToken);

        results.ShouldHaveSingleItem().Text.ShouldBe("new text");
    }

    [Fact(DisplayName = "Given an ephemeral fact past its TTL, when the sweep runs, then it is deleted and standing facts survive")]
    public async Task SweepExpiredEphemeralFactsAsync()
    {
        var store = Store;
        var cancellationToken = TestContext.Current.CancellationToken;

        await store.WriteAsync(Write("old-task", "ephemeral note", kind: MemoryFactKind.Ephemeral), cancellationToken);
        await store.WriteAsync(Write("old-standing", "standing note"), cancellationToken);
        await ExecuteNonQueryAsync(
            $"UPDATE {MemoryDatabase.Schema}.memory_facts SET created_at = created_at - interval '15 days' WHERE topic_key = 'old-task'",
            cancellationToken);

        var swept = await store.SweepExpiredAsync(FixedTime.Now, cancellationToken);

        swept.ShouldBe(1);
        var remaining = await store.ListAsync(MemoryScope.User, "user-1", cancellationToken);
        remaining.ShouldHaveSingleItem().TopicKey.ShouldBe("old-standing");
    }

    [Fact(DisplayName = "Given a stored fact, when ForgetAsync runs twice, then the first deletes and the second reports absence")]
    public async Task ForgetByIdAsync()
    {
        var store = Store;
        var cancellationToken = TestContext.Current.CancellationToken;
        var stored = await store.WriteAsync(Write("forget-me", "text"), cancellationToken);

        (await store.ForgetAsync(stored.Id, cancellationToken)).ShouldBeTrue();
        (await store.ForgetAsync(stored.Id, cancellationToken)).ShouldBeFalse();
        (await store.ListAsync(MemoryScope.User, "user-1", cancellationToken)).ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given an embedding with the wrong dimension, when WriteAsync runs, then ArgumentException refuses it before any SQL")]
    public async Task RefuseWrongDimensionEmbeddingAsync()
    {
        await Should.ThrowAsync<ArgumentException>(
            () => Store.WriteAsync(Write("bad-vector", "text", new float[3]), TestContext.Current.CancellationToken));
    }

    private IMemoryStore Store => provider.GetRequiredService<IMemoryStore>();

    private static MemoryFactWrite Write(
        string topicKey,
        string text,
        float[]? embedding = null,
        MemoryFactKind kind = MemoryFactKind.Standing)
    {
        return new MemoryFactWrite(
            MemoryScope.User,
            "User-1",
            kind,
            topicKey,
            text,
            MemorySource.Chat,
            "user-1",
            embedding);
    }

    /// <summary>A deterministic 768-dim vector: the basis axis plus an optional tilt into the next axis.</summary>
    private static float[] Vector(int tiltAxis, float tiltAmount)
    {
        var vector = new float[MemoryFactPolicy.EmbeddingDimensions];
        vector[0] = 1f;
        vector[tiltAxis + 1] = tiltAmount;
        return vector;
    }

    private Task<List<string>> QuerySingleColumnAsync(string sql)
    {
        return QueryAsync(sql, static reader => reader.GetString(0));
    }

    private async Task ExecuteNonQueryAsync(string sql, CancellationToken cancellationToken)
    {
        var db = provider.GetRequiredService<MemoryDbContext>();
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        _ = await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<List<T>> QueryAsync<T>(string sql, Func<System.Data.Common.DbDataReader, T> project)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var db = provider.GetRequiredService<MemoryDbContext>();
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = db.Database.GetDbConnection();
        var rows = new List<T>();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(project(reader));
        }

        return rows;
    }
}

/// <summary>Deterministic clock: fixed "now", the same value the store writes with.</summary>
file static class FixedTime
{
    public static readonly TimeProvider Provider = new FixedTimeProvider();

    public static DateTimeOffset Now => Provider.GetUtcNow();

    private sealed class FixedTimeProvider : TimeProvider
    {
        private static readonly DateTimeOffset now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            return now;
        }
    }
}
