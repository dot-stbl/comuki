using Comuki.Modules.Memory.Domain.Facts;
using Comuki.Modules.Memory.Domain.Facts.Kinds;
using Comuki.Modules.Memory.Domain.Facts.Scopes;
using Comuki.Modules.Memory.Domain.Facts.Sources;
using Comuki.Modules.Memory.Domain.Ids;
using Npgsql;

namespace Comuki.Modules.Memory.Infrastructure.Persistence.Stores;

/// <summary>
/// Boot-time seeder for the platform's standing self-knowledge: after the
/// boot migrations and before the host starts serving, a handful of
/// <c>platform.*</c> facts are written into <c>memory.memory_facts</c> so
/// the brain knows what Comuki is from the very first turn. Idempotent
/// per <c>topic_key</c>: an identical stored text is skipped, a changed
/// text (e.g. a version bump in <c>platform.identity</c>) supersedes the
/// previous row through the store's normal supersede mechanism — history
/// is kept, exactly as an explicit write would keep it.
/// </summary>
/// <remarks>
/// Raw SQL over the same connection string the migrations just used
/// (Npgsql pools per connection string — no second pool): at this point
/// of the boot there is no DI scope yet, and the round-trip is three
/// rows. The <c>platform.</c> topic-key prefix is reserved for this
/// seeder — chat / run writers never produce it.
/// </remarks>
public static class MemorySeeder
{
    /// <summary>Reserved topic-key namespace of the boot seeder.</summary>
    public const string TopicPrefix = "platform.";

    private const string IdentityTopic = TopicPrefix + "identity";
    private const string ArchitectureTopic = TopicPrefix + "architecture";
    private const string ConventionsTopic = TopicPrefix + "conventions";

    private const string IdentityText =
        "Comuki is an agent orchestration platform where a leading model (brain) decomposes tasks "
        + "and conducts a swarm of ephemeral workers in containers. Version: {VERSION}. "
        + "Built with .NET 10 + React 19.";

    private const string ArchitectureText =
        "Modules: Identity (auth/users/keys), Orchestration (runs/work-items/leases), Intake (sources/tickets), "
        + "Chat (operator↔brain sessions), Knowledge (RAG with pgvector), Memory (facts with TTL), "
        + "Costs (usage tracking), Artifacts (MinIO bundles), Scheduler (cron jobs), Proxy (virtual keys). "
        + "Compute: Docker or Kubernetes batch Jobs.";

    private const string ConventionsText =
        "Commits follow [.stbl](type/scope): subject format. Build gate: 0 warnings, 0 errors. "
        + "Tests: xUnit v3 + Shouldly. Frontend: TanStack Query + Archivo + JetBrains Mono.";

    /// <summary>Seeds the platform facts; safe to run on every boot.</summary>
    /// <param name="connectionString">Postgres connection string (the same one the boot migrations used).</param>
    /// <param name="version">Build version stamped into the identity fact so upgrades supersede it.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    public static async Task<MemorySeedResult> SeedAsync(string connectionString, string version, CancellationToken cancellationToken)
    {
        var facts = new SeedFact[]
        {
            new(IdentityTopic, IdentityText.Replace("{VERSION}", version, StringComparison.Ordinal)),
            new(ArchitectureTopic, ArchitectureText),
            new(ConventionsTopic, ConventionsText),
        };

        var written = 0;
        var superseded = 0;
        var unchanged = 0;

        // One timestamp for the whole pass: a superseding upgrade writes
        // superseded_at == the replacement's created_at — the two rows
        // read as one atomic fact change.
        var now = DateTimeOffset.UtcNow;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        foreach (var fact in facts)
        {
            if (await FindActiveAsync(connection, transaction, fact.TopicKey, cancellationToken) is not { } active)
            {
                await InsertAsync(connection, transaction, fact.TopicKey, fact.Text, now, cancellationToken);
                written++;
            }
            else if (active.Text == fact.Text)
            {
                unchanged++;
            }
            else
            {
                await SupersedeAsync(connection, transaction, active.Id, now, cancellationToken);
                await InsertAsync(connection, transaction, fact.TopicKey, fact.Text, now, cancellationToken);
                superseded++;
            }
        }

        await transaction.CommitAsync(cancellationToken);

        return new MemorySeedResult(written, superseded, unchanged);
    }

    private static async Task<ActiveFact?> FindActiveAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string topicKey,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = SelectActiveSql;
        command.Parameters.AddWithValue("scope", MemoryScopeKeys.Key(MemoryScope.Global));
        command.Parameters.AddWithValue("subject", MemoryScopeKeys.GlobalSubject);
        command.Parameters.AddWithValue("topic", MemoryFact.CanonicalKey(topicKey));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return !await reader.ReadAsync(cancellationToken) ? null : new ActiveFact(reader.GetGuid(0), reader.GetString(1));
    }

    private static async Task InsertAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string topicKey,
        string text,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = InsertSql;
        command.Parameters.AddWithValue("id", MemoryFactId.New().Value);
        command.Parameters.AddWithValue("scope", MemoryScopeKeys.Key(MemoryScope.Global));
        command.Parameters.AddWithValue("subject", MemoryScopeKeys.GlobalSubject);
        command.Parameters.AddWithValue("kind", MemoryFactKindKeys.Standing);
        command.Parameters.AddWithValue("topic", MemoryFact.CanonicalKey(topicKey));
        command.Parameters.AddWithValue("text", text.Trim());
        command.Parameters.AddWithValue("source", MemorySourceKeys.Seeder);
        command.Parameters.AddWithValue("createdBy", "seeder");
        command.Parameters.AddWithValue("now", now);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task SupersedeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid supersededId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = SupersedeSql;
        command.Parameters.AddWithValue("id", supersededId);
        command.Parameters.AddWithValue("now", now);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string SelectActiveSql =
        "SELECT id, text FROM " + MemoryDatabase.Schema + "." + MemoryDatabase.MemoryFacts + " "
        + "WHERE scope = @scope AND subject_id = @subject AND topic_key = @topic AND superseded_at IS NULL";

    private const string InsertSql =
        "INSERT INTO " + MemoryDatabase.Schema + "." + MemoryDatabase.MemoryFacts
        + " (id, scope, subject_id, kind, topic_key, text, source, created_by, created_at) "
        + "VALUES (@id, @scope, @subject, @kind, @topic, @text, @source, @createdBy, @now)";

    private const string SupersedeSql =
        "UPDATE " + MemoryDatabase.Schema + "." + MemoryDatabase.MemoryFacts
        + " SET superseded_at = @now WHERE id = @id AND superseded_at IS NULL";

    /// <summary>The one active row a topic key resolved to.</summary>
    /// <param name="Id">Row id — used to target the supersede UPDATE.</param>
    /// <param name="Text">Stored text — compared against the seed text.</param>
    private sealed record ActiveFact(Guid Id, string Text);

    /// <summary>One seed fact to upsert.</summary>
    /// <param name="TopicKey">Reserved <c>platform.*</c> topic key.</param>
    /// <param name="Text">Standing fact text.</param>
    private sealed record SeedFact(string TopicKey, string Text);
}
