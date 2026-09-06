using Comuki.Modules.Knowledge.Domain;
using Comuki.Modules.Knowledge.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Modules.Knowledge.Infrastructure.Persistence;

/// <summary>
/// EF model for the Knowledge schema: source_documents / memory_embeddings.
/// Snake_case naming is applied by the shared options recipe
/// (<see cref="ApplyOptions"/>) via <c>UseSnakeCaseNamingConvention</c>;
/// column names are still written explicitly in the configurations so
/// migration snapshots stay stable. The pgvector <c>embedding</c> column
/// lives OUTSIDE the EF model — created and queried through raw SQL
/// (see <see cref="Stores.EmbeddingSql"/>) so the module needs no
/// EF-pgvector provider.
/// </summary>
/// <param name="options"></param>
public sealed class KnowledgeDbContext(DbContextOptions<KnowledgeDbContext> options)
    : DbContext(options)
{
    /// <summary>Knowledge-base source documents — git | upload | url pointers.</summary>
    public DbSet<SourceDocument> SourceDocuments => Set<SourceDocument>();

    /// <summary>Knowledge-base embedded chunks — pgvector embedding column, raw-SQL managed.</summary>
    public DbSet<MemoryEmbedding> MemoryEmbeddings => Set<MemoryEmbedding>();

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
            .UseNpgsql(connectionString, static npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", KnowledgeDatabase.Schema))
            .UseSnakeCaseNamingConvention();
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .ApplyConfiguration(new SourceDocumentConfiguration())
            .ApplyConfiguration(new MemoryEmbeddingConfiguration());
        base.OnModelCreating(modelBuilder);
    }
}
