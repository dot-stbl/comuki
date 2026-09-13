using Comuki.Modules.Memory.Domain.Chat;
using Comuki.Modules.Memory.Domain.Facts;
using Comuki.Modules.Memory.Domain.Facts.Scopes;
using Comuki.Modules.Memory.Domain.Learning;
using Comuki.Modules.Memory.Infrastructure.Persistence.Configurations;
using Comuki.Shared.Kernel.Scoping;
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
/// <para>
/// <see cref="MemoryFact"/> carries the object-axis query filter — the
/// same discipline <c>KnowledgeDbContext</c> and eight other DbContexts
/// in the solution already enforce, previously missing here entirely.
/// <see cref="MemoryFact.Scope"/>/<see cref="MemoryFact.SubjectId"/> only
/// partially map onto <see cref="SubjectScope"/>'s axis (project
/// membership, not a subject identity): <see cref="MemoryScope.Global"/>
/// rows are visible to everyone (mirrors Knowledge's null-project
/// corpus); <see cref="MemoryScope.Project"/> rows follow
/// <see cref="SubjectScope.ProjectIds"/> the same way a Knowledge
/// document does; <see cref="MemoryScope.User"/> rows have no project to
/// check against — <see cref="SubjectScope"/> carries no per-user
/// identity axis at all — so they are visible only to an unrestricted
/// (system) caller. That is the fail-closed choice, not an oversight:
/// a restricted caller can never prove "this row is mine" through this
/// filter, so it never gets to see User-scoped rows through it.
/// <see cref="ChatMessage"/>, <see cref="ChatCheckpoint"/>
/// and <see cref="LearningCandidate"/> carry no
/// ownership axis at all (session-keyed or global queue state) and are
/// deliberately left unfiltered.
/// </para>
/// </summary>
/// <param name="options"></param>
/// <param name="scopeAccessor">
/// Ambient subject scope (singleton; state in <see cref="AsyncLocal{T}"/>).
/// Optional so direct construction — the Migrator, design-time
/// factories, test fixtures — keeps compiling; a context built without
/// an accessor is by definition a system consumer and sees everything.
/// A context built WITH one (the host DI) fails loudly on a flow that
/// established no scope: workers must declare
/// <see cref="ISubjectScopeAccessor.AsSystem"/>, request paths get their
/// scope from the host middleware.
/// </param>
public sealed class MemoryDbContext(
    DbContextOptions<MemoryDbContext> options,
    ISubjectScopeAccessor? scopeAccessor = null)
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
    /// Left disjunct of the scope filter: true when the current subject
    /// sees every project (a platform-scope role, a system consumer, or a
    /// directly-constructed system context).
    /// </summary>
    public bool ScopeUnrestricted => scopeAccessor?.Current.Unrestricted ?? true;

    /// <summary>
    /// The current subject's assigned projects, canonicalized the same
    /// way <see cref="MemoryFact.SubjectId"/> is
    /// (<see cref="MemoryFact.CanonicalKey"/>) so a
    /// <see cref="MemoryScope.Project"/> row's string subject id can be
    /// compared directly — EF Core cannot translate a
    /// <c>Guid.Parse</c> of the column into SQL, so the comparison runs
    /// the other way around. Re-materialised per read, not a walk.
    /// </summary>
    public string[] ScopeProjectSubjectKeys => scopeAccessor is { } accessor
        ? [.. accessor.Current.ProjectIds.Select(static projectId => MemoryFact.CanonicalKey(projectId.Value.ToString()))]
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

        // The object axis: global facts are everyone's (mirrors
        // Knowledge's null-project corpus); project facts follow the
        // project axis SubjectScope already models; user facts have no
        // axis to check here and so are visible only when unrestricted.
        modelBuilder.Entity<MemoryFact>()
            .HasQueryFilter(fact => ScopeUnrestricted
                || fact.Scope == MemoryScope.Global
                || (fact.Scope == MemoryScope.Project && ScopeProjectSubjectKeys.Contains(fact.SubjectId)));

        base.OnModelCreating(modelBuilder);
    }
}
