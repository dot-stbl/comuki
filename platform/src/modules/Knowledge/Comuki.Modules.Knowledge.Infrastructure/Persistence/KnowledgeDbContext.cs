using Comuki.Modules.Knowledge.Domain;
using Comuki.Modules.Knowledge.Infrastructure.Persistence.Configurations;
using Comuki.Shared.Kernel.Scoping;
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
/// EF-pgvector provider. Every entity carries the global subject-scope
/// query filter — the object axis of the authorization model. Both
/// entities carry a nullable <c>ProjectId</c> (a null project is the
/// global corpus, visible to every subject); rows with a project follow
/// that project's axis.
/// </summary>
/// <param name="options"></param>
/// <param name="scopeAccessor">
/// Ambient subject scope (singleton; state in <see cref="AsyncLocal{T}"/>).
/// Optional so direct construction — the Migrator, design-time factories,
/// test fixtures — keeps compiling; a context built without an accessor is
/// by definition a system consumer and sees everything. A context built
/// WITH one (the host DI) fails loudly on a flow that established no
/// scope: workers must declare <see cref="ISubjectScopeAccessor.AsSystem"/>,
/// request paths get their scope from the host middleware.
/// </param>
public sealed class KnowledgeDbContext(
    DbContextOptions<KnowledgeDbContext> options,
    ISubjectScopeAccessor? scopeAccessor = null)
    : DbContext(options)
{
    /// <summary>Knowledge-base source documents — git | upload | url pointers.</summary>
    public DbSet<SourceDocument> SourceDocuments => Set<SourceDocument>();

    /// <summary>Knowledge-base embedded chunks — pgvector embedding column, raw-SQL managed.</summary>
    public DbSet<MemoryEmbedding> MemoryEmbeddings => Set<MemoryEmbedding>();

    /// <summary>
    /// Left disjunct of the scope filter: true when the current subject
    /// sees every project (a platform-scope role, a system consumer, or a
    /// directly-constructed system context).
    /// </summary>
    public bool ScopeUnrestricted => scopeAccessor?.Current.Unrestricted ?? true;

    /// <summary>
    /// Projects the current subject is confined to, projected onto the
    /// raw <see cref="Guid"/> values EF Core can translate. Empty means
    /// "no project", not "any project". Re-materialised per read — a copy of
    /// the already-resolved scope, not a walk. The query filters below
    /// compare against this Guid array (not the strongly-typed
    /// <c>ProjectId</c>) because EF Core's query translator cannot lower
    /// a custom struct's member access into SQL.
    /// </summary>
    public Guid[] ScopeProjectIds => scopeAccessor is { } accessor
        ? [.. accessor.Current.ProjectIds.Select(static projectId => projectId.Value)]
        : [];

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

        // The object axis, as row-level filters: a null project means the
        // corpus is global (cross-project — visible to every subject);
        // otherwise the row follows its project axis. ProjectId is a raw
        // nullable Guid on both entities; the scope list is a Guid array so
        // Contains lowers to `= ANY(...)` in SQL.
        modelBuilder.Entity<SourceDocument>()
            .HasQueryFilter(document => ScopeUnrestricted
                || document.ProjectId == null
                || ScopeProjectIds.Contains(document.ProjectId.Value));
        modelBuilder.Entity<MemoryEmbedding>()
            .HasQueryFilter(embedding => ScopeUnrestricted
                || embedding.ProjectId == null
                || ScopeProjectIds.Contains(embedding.ProjectId.Value));

        base.OnModelCreating(modelBuilder);
    }
}
