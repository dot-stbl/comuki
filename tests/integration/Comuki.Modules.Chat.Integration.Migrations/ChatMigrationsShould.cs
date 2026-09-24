using Comuki.Modules.Chat.Domain.Messages;
using Comuki.Modules.Chat.Domain.Sessions;
using Comuki.Modules.Chat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace Comuki.Modules.Chat.Integration.Migrations;

/// <summary>
/// Proves the Chat EF migrations apply on a real Postgres, and — the one
/// thing a plain schema-shape assertion cannot — that the
/// <c>ChatMessageParts</c> migration's hand-authored <c>USING</c> backfill
/// (integer ordinal &lt;-&gt; enum member name, for <c>chat_sessions.status</c>
/// and <c>chat_messages.role</c>) round-trips correctly in BOTH directions
/// against rows this test seeds itself, by driving <see cref="IMigrator"/>
/// down to the previous migration and back up.
/// </summary>
public sealed class ChatMigrationsShould : IAsyncLifetime
{
    private const string PreviousMigrationId = "20260903085537_UseSchemas";
    private const string PartsMigrationId = "20260911020202_ChatMessageParts";

    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    /// <summary>boundary: initialised in InitializeAsync before any test runs</summary>
    private ChatDbContext db = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await container.StartAsync(cancellationToken);

        // Direct construction only — AddChatPersistence also compiles the Voluta chat graph and pulls in AddChatApplication's ports, none of which this test needs (schema + IMigrator is enough).
        var options = new DbContextOptionsBuilder<ChatDbContext>();
        ChatDbContext.ApplyOptions(options, container.GetConnectionString());
        db = new ChatDbContext(options.Options);
        await db.Database.MigrateAsync(cancellationToken);
        await db.Database.OpenConnectionAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await db.DisposeAsync();
        await container.DisposeAsync();
    }

    [Fact(DisplayName = "Given an empty database, when the chat context migrates, then the tables, history and rich-shape columns exist")]
    public async Task CreateExpectedSchemaAsync()
    {
        var tables = await QuerySingleColumnAsync(
            $"SELECT table_name FROM information_schema.tables WHERE table_schema = '{ChatDatabase.Schema}' ORDER BY table_name");

        tables.ShouldContain(ChatDatabase.Sessions);
        tables.ShouldContain(ChatDatabase.Messages);
        tables.ShouldContain(ChatDatabase.Checkpoints);
        tables.ShouldContain("__ef_migrations_history");

        var messageColumns = await QueryColumnsAsync(ChatDatabase.Schema, ChatDatabase.Messages);
        messageColumns["role"].ShouldBe(new ColumnSpec("character varying", "NO"));
        messageColumns["parts"].ShouldBe(new ColumnSpec("jsonb", "YES"));
        messageColumns["meta"].ShouldBe(new ColumnSpec("jsonb", "YES"));

        var sessionColumns = await QueryColumnsAsync(ChatDatabase.Schema, ChatDatabase.Sessions);
        sessionColumns["status"].ShouldBe(new ColumnSpec("character varying", "NO"));
    }

    [Fact(DisplayName = "Given rows written on the current schema, when the migrator rolls the ChatMessageParts migration back and forward again, then role/status backfill correctly in both directions and parts/meta round-trip through the column drop as null")]
    public async Task RoundtripRoleStatusAndPartsAcrossDownAndUpAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;

        var session = ChatSession.Create(null, Guid.NewGuid(), "Roundtrip", now);
        var archivedSession = ChatSession.Create(null, Guid.NewGuid(), "Roundtrip (archived)", now);
        archivedSession.Archive(now);
        db.Sessions.AddRange(session, archivedSession);
        await db.SaveChangesAsync(cancellationToken);

        var userMessage = ChatMessage.Create(
            session.Id, ChatMessageRole.User, "hi", null, now,
            partsJson: /*lang=json,strict*/ """[{"kind":"text","text":"hi"}]""");
        var assistantMessage = ChatMessage.Create(
            session.Id, ChatMessageRole.Assistant, "hello", null, now,
            metaJson: /*lang=json,strict*/ """{"model":"brain-stub"}""");
        var systemMessage = ChatMessage.Create(session.Id, ChatMessageRole.System, "digest", null, now);
        var toolMessage = ChatMessage.Create(session.Id, ChatMessageRole.Tool, "ran", "create_ticket", now);
        db.Messages.AddRange(userMessage, assistantMessage, systemMessage, toolMessage);
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();

        var migrator = db.GetService<IMigrator>();

        // --- Down: character varying -> integer, USING the reverse CASE map ---
        await migrator.MigrateAsync(PreviousMigrationId, cancellationToken);

        var sessionColumnsDown = await QueryColumnsAsync(ChatDatabase.Schema, ChatDatabase.Sessions);
        sessionColumnsDown["status"].DataType.ShouldBe("integer");
        var messageColumnsDown = await QueryColumnsAsync(ChatDatabase.Schema, ChatDatabase.Messages);
        messageColumnsDown["role"].DataType.ShouldBe("integer");
        messageColumnsDown.ShouldNotContainKey("parts");
        messageColumnsDown.ShouldNotContainKey("meta");

        (await QueryTextAsync($"SELECT status::text FROM {ChatDatabase.Schema}.chat_sessions WHERE id = @id", session.Id.Value)).ShouldBe("0");
        (await QueryTextAsync($"SELECT status::text FROM {ChatDatabase.Schema}.chat_sessions WHERE id = @id", archivedSession.Id.Value)).ShouldBe("1");
        (await QueryTextAsync($"SELECT role::text FROM {ChatDatabase.Schema}.chat_messages WHERE id = @id", userMessage.Id)).ShouldBe("0");
        (await QueryTextAsync($"SELECT role::text FROM {ChatDatabase.Schema}.chat_messages WHERE id = @id", assistantMessage.Id)).ShouldBe("1");
        (await QueryTextAsync($"SELECT role::text FROM {ChatDatabase.Schema}.chat_messages WHERE id = @id", systemMessage.Id)).ShouldBe("2");
        (await QueryTextAsync($"SELECT role::text FROM {ChatDatabase.Schema}.chat_messages WHERE id = @id", toolMessage.Id)).ShouldBe("3");

        // --- Up: integer -> character varying, USING the forward CASE map ---
        await migrator.MigrateAsync(PartsMigrationId, cancellationToken);

        var sessionColumnsUp = await QueryColumnsAsync(ChatDatabase.Schema, ChatDatabase.Sessions);
        sessionColumnsUp["status"].ShouldBe(new ColumnSpec("character varying", "NO"));
        var messageColumnsUp = await QueryColumnsAsync(ChatDatabase.Schema, ChatDatabase.Messages);
        messageColumnsUp["role"].ShouldBe(new ColumnSpec("character varying", "NO"));
        messageColumnsUp["parts"].ShouldBe(new ColumnSpec("jsonb", "YES"));
        messageColumnsUp["meta"].ShouldBe(new ColumnSpec("jsonb", "YES"));

        (await QueryTextAsync($"SELECT status::text FROM {ChatDatabase.Schema}.chat_sessions WHERE id = @id", session.Id.Value)).ShouldBe(nameof(ChatSessionStatus.Active));
        (await QueryTextAsync($"SELECT status::text FROM {ChatDatabase.Schema}.chat_sessions WHERE id = @id", archivedSession.Id.Value)).ShouldBe(nameof(ChatSessionStatus.Archived));
        (await QueryTextAsync($"SELECT role::text FROM {ChatDatabase.Schema}.chat_messages WHERE id = @id", userMessage.Id)).ShouldBe(nameof(ChatMessageRole.User));
        (await QueryTextAsync($"SELECT role::text FROM {ChatDatabase.Schema}.chat_messages WHERE id = @id", assistantMessage.Id)).ShouldBe(nameof(ChatMessageRole.Assistant));
        (await QueryTextAsync($"SELECT role::text FROM {ChatDatabase.Schema}.chat_messages WHERE id = @id", systemMessage.Id)).ShouldBe(nameof(ChatMessageRole.System));
        (await QueryTextAsync($"SELECT role::text FROM {ChatDatabase.Schema}.chat_messages WHERE id = @id", toolMessage.Id)).ShouldBe(nameof(ChatMessageRole.Tool));

        // The dropped-then-re-added jsonb columns do not resurrect old
        // payloads across the round trip — only the schema comes back,
        // never the data (see design.md "Enum columns" / "Why not owned
        // types" — the Down direction is documented as lossy for parts/meta).
        // Assert that explicitly so a future edit to the migration cannot
        // silently start claiming otherwise.
        var remaining = await QueryTextAsync(
            $"SELECT count(*)::text FROM {ChatDatabase.Schema}.chat_messages WHERE parts IS NOT NULL OR meta IS NOT NULL");
        remaining.ShouldBe("0");
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

    private async Task<string?> QueryTextAsync(string sql, Guid? parameter = null)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (parameter is { } value)
        {
            var dbParameter = command.CreateParameter();
            dbParameter.ParameterName = "@id";
            dbParameter.Value = value;
            command.Parameters.Add(dbParameter);
        }

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result as string;
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
