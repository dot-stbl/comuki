namespace Comuki.Modules.Memory.Infrastructure.Persistence;

/// <summary>
/// Physical Memory database — the Postgres schema name plus every table that
/// belongs to it. Single source every <c>IEntityTypeConfiguration</c> reads;
/// no magic strings in <c>builder.ToTable(...)</c>. The migration history
/// table lives at <c>memory.__ef_migrations_history</c> (per the EF Core
/// Postgres convention) and is configured via
/// <c>npgsql.MigrationsHistoryTable(name, schema)</c> in
/// <see cref="MemoryDbContext.ApplyOptions"/>.
/// </summary>
public static class MemoryDatabase
{
    /// <summary>Postgres schema name. the namespace.</summary>
    public const string Schema = "memory";

    /// <summary>Chat session messages (short-term memory).</summary>
    public const string ChatMessages = "chat_messages";

    /// <summary>Voluta graph state per chat session (jsonb).</summary>
    public const string ChatCheckpoints = "chat_checkpoints";

    /// <summary>Long-term memory facts (+ pgvector embedding column, raw-SQL managed).</summary>
    public const string MemoryFacts = "memory_facts";

    /// <summary>Learning-loop candidate queue (pending → approved | rejected).</summary>
    public const string LearningCandidates = "learning_candidates";
}
