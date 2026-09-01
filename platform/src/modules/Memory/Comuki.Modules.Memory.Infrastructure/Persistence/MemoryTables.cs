namespace Comuki.Modules.Memory.Infrastructure.Persistence;

/// <summary>
/// Physical Memory table names — the single source every EF configuration
/// reads; no magic strings in <c>IEntityTypeConfiguration</c>. Includes the
/// module-private migrations history table: the fourth context (after
/// orchestration, identity, projects) migrating the same database.
/// </summary>
public static class MemoryTables
{
    /// <summary>Chat session messages (short-term memory).</summary>
    public const string ChatMessages = "chat_messages";

    /// <summary>Voluta graph state per chat session (jsonb).</summary>
    public const string ChatCheckpoints = "chat_checkpoints";

    /// <summary>Long-term memory facts (+ pgvector embedding column, raw-SQL managed).</summary>
    public const string MemoryFacts = "memory_facts";

    /// <summary>Learning-loop candidate queue (pending → approved | rejected).</summary>
    public const string LearningCandidates = "learning_candidates";

    /// <summary>Module-private EF migrations history table.</summary>
    public const string MigrationsHistory = "__comuki_memory";
}
