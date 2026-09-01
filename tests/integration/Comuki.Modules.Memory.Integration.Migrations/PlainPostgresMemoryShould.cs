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
/// The add-chat-memory hard floor: on a PLAIN postgres image (no
/// pgvector available) the migration still succeeds, the embedding
/// column is simply skipped, writes WITH embeddings degrade to
/// embedding-less rows, and search answers via the fallback ranking —
/// memory must never require pgvector.
/// </summary>
public sealed class PlainPostgresMemoryShould : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    /// <summary>boundary: initialised in InitializeAsync before any test runs</summary>
    private ServiceProvider provider = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await container.StartAsync(cancellationToken);

        var services = new ServiceCollection();
        _ = services.AddMemoryPersistence(container.GetConnectionString());
        _ = services.AddSingleton(TimeProvider.System);
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

    [Fact(DisplayName = "Given plain postgres, when the memory context migrates, then all tables exist and the embedding column is skipped")]
    public async Task MigrateWithoutPgvectorAsync()
    {
        var columns = await QuerySingleColumnAsync(
            "SELECT column_name FROM information_schema.columns "
            + "WHERE table_schema = 'public' AND table_name = 'memory_facts' AND column_name = 'embedding'");

        columns.ShouldBeEmpty();

        var tables = await QuerySingleColumnAsync(
            "SELECT table_name FROM information_schema.tables "
            + "WHERE table_schema = 'public' AND table_name IN ('chat_messages','chat_checkpoints','memory_facts','learning_candidates') "
            + "ORDER BY table_name");
        tables.Count.ShouldBe(4);
    }

    [Fact(DisplayName = "Given a write carrying an embedding, when pgvector is absent, then the fact lands without a vector and stays searchable")]
    public async Task DegradeEmbeddingWriteWithoutPgvectorAsync()
    {
        var store = provider.GetRequiredService<IMemoryStore>();
        var cancellationToken = TestContext.Current.CancellationToken;
        var embedding = new float[MemoryFactPolicy.EmbeddingDimensions];
        embedding[0] = 1f;

        var stored = await store.WriteAsync(
            new MemoryFactWrite(
                MemoryScope.User,
                "user-1",
                MemoryFactKind.Standing,
                "degraded",
                "text without a vector",
                MemorySource.Chat,
                "user-1",
                embedding),
            cancellationToken);

        stored.Text.ShouldBe("text without a vector");

        // a query WITH an embedding must still answer through the fallback
        var results = await store.SearchAsync(
            new MemoryFactQuery(Scope: MemoryScope.User, SubjectId: "user-1", Embedding: embedding),
            cancellationToken);

        results.ShouldHaveSingleItem().Id.ShouldBe(stored.Id);
    }

    [Fact(DisplayName = "Given plain postgres, when facts are written and listed, then supersede and fallback ranking behave as on pgvector")]
    public async Task KeepStoreSemanticsWithoutPgvectorAsync()
    {
        var store = provider.GetRequiredService<IMemoryStore>();
        var cancellationToken = TestContext.Current.CancellationToken;

        await store.WriteAsync(
            Write("shared-topic", "first"), cancellationToken);
        await store.WriteAsync(
            Write("shared-topic", "second"), cancellationToken);
        await store.WriteAsync(
            Write("other-topic", "other", kind: MemoryFactKind.Ephemeral), cancellationToken);

        var visible = await store.ListAsync(MemoryScope.User, "user-1", cancellationToken);

        visible.Count.ShouldBe(2);
        visible.Select(static fact => fact.Text).ShouldBe(["second", "other"]);
    }

    private static MemoryFactWrite Write(string topicKey, string text, MemoryFactKind kind = MemoryFactKind.Standing)
    {
        return new MemoryFactWrite(
            MemoryScope.User,
            "user-1",
            kind,
            topicKey,
            text,
            MemorySource.Human,
            "user-1");
    }

    private async Task<List<string>> QuerySingleColumnAsync(string sql)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var db = provider.GetRequiredService<MemoryDbContext>();
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
}
