using Comuki.Modules.Memory.Domain.Chat;
using Comuki.Modules.Memory.Domain.Facts;
using Comuki.Modules.Memory.Domain.Learning;
using Comuki.Modules.Memory.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Modules.Memory.Infrastructure.Persistence;

/// <summary>
/// EF model for the Memory schema: chat_messages / chat_checkpoints /
/// memory_facts / learning_candidates. Snake_case naming is applied by
/// the shared options recipe (<see cref="ApplyOptions"/>) via
/// <c>UseSnakeCaseNamingConvention</c>; column names are still written
/// explicitly in the configurations so migration snapshots stay stable.
/// The pgvector <c>embedding</c> column lives OUTSIDE the EF model —
/// created and queried through raw SQL (see <c>MemoryFactSql</c>) so
/// the module needs no EF-pgvector provider. The knowledge-base tables
/// (<c>source_documents</c> / <c>memory_embeddings</c>) live in the
/// <c>knowledge</c> schema and are owned by <c>KnowledgeDbContext</c>.
/// </summary>
/// <param name="options"></param>
public sealed class MemoryDbContext(DbContextOptions<MemoryDbContext> options)
    : DbContext(options)
{
    /// <summary>Chat messages — every message of every session.</summary>
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

    /// <summary>Chat checkpoints — one graph-state snapshot per session.</summary>
    public DbSet<ChatCheckpoint> ChatCheckpoints => Set<ChatCheckpoint>();

    /// <summary>Memory facts — long-term memory with supersede semantics.</summary>
    public DbSet<MemoryFact> MemoryFacts => Set<MemoryFact>();

    /// <summary>Learning candidates — the human-approval rule queue.</summary>
    public DbSet<LearningCandidate> LearningCandidates => Set<LearningCandidate>();

    /// <summary>
    /// Single options recipe (Npgsql + snake_case + private history table)
    /// used by the DI extension, the design-time factory and the Migrator —
    /// one place, no drift.
    /// </summary>
    /// <param name="builder"></param>
    /// <param name="connectionString"></param>
    public static void ApplyOptions(DbContextOptionsBuilder builder, string connectionString)
    {
        builder
.UseNpgsql(connectionString, static npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", MemoryDatabase.Schema))
            .UseSnakeCaseNamingConvention();
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .ApplyConfiguration(new ChatMessageConfiguration())
            .ApplyConfiguration(new ChatCheckpointConfiguration())
            .ApplyConfiguration(new MemoryFactConfiguration())
            .ApplyConfiguration(new LearningCandidateConfiguration());
        base.OnModelCreating(modelBuilder);
    }
}
