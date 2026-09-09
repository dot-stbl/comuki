using Comuki.Modules.Chat.Domain.Messages;
using Comuki.Modules.Chat.Domain.Sessions;
using Comuki.Modules.Chat.Infrastructure.Persistence.Configurations;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.EntityFrameworkCore;
using Voluta.Checkpoints.EntityFrameworkCore;

namespace Comuki.Modules.Chat.Infrastructure.Persistence;

/// <summary>
/// EF model for the Chat schema: chat_sessions / chat_messages plus the
/// Voluta checkpoint table (re-mapped onto <c>chat_checkpoints</c> per the
/// memory contract — the package default is <c>voluta_checkpoints</c>).
/// The context implements <see cref="IVolutaCheckpointDbContext"/> so the
/// EF Core checkpointer rides the same Npgsql connection and the module's
/// own migrations history. Snake_case naming is applied by the shared
/// options recipe (<see cref="ApplyOptions"/>) via
/// <c>UseSnakeCaseNamingConvention</c>. Every owned entity carries the
/// global subject-scope query filter — the object axis of the
/// authorization model. Sessions are project-scoped (a nullable
/// <c>ProjectId</c>; a null project means a cross-project chat — visible
/// to every subject). Messages follow their session's scope through a
/// navigation filter.
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
public sealed class ChatDbContext(
    DbContextOptions<ChatDbContext> options,
    ISubjectScopeAccessor? scopeAccessor = null)
    : DbContext(options), IVolutaCheckpointDbContext
{
    /// <summary>Sessions — aggregate roots.</summary>
    public DbSet<ChatSession> Sessions => Set<ChatSession>();

    /// <summary>Append-only transcript.</summary>
    public DbSet<ChatMessage> Messages => Set<ChatMessage>();

    /// <summary>Voluta graph checkpoints (thread = session id).</summary>
    public DbSet<CheckpointRecord> Checkpoints => Set<CheckpointRecord>();

    /// <summary>
    /// Left disjunct of the scope filter: true when the current subject
    /// sees every project (a platform-scope role, a system consumer, or a
    /// directly-constructed system context).
    /// </summary>
    public bool ScopeUnrestricted => scopeAccessor?.Current.Unrestricted ?? true;

    /// <summary>
    /// Projects the current subject is confined to; empty means "no
    /// project", not "any project". Re-materialised per read — a copy of
    /// the already-resolved scope, not a walk.
    /// </summary>
    public ProjectId[] ScopeProjectIds => scopeAccessor is { } accessor
        ? [.. accessor.Current.ProjectIds]
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
            .UseNpgsql(connectionString, static npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", ChatDatabase.Schema))
            .UseSnakeCaseNamingConvention();
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .ApplyConfiguration(new ChatSessionConfiguration())
            .ApplyConfiguration(new ChatMessageConfiguration())
            .ApplyVolutaCheckpointModel();

        // memory-contract table name (in the chat schema) instead of the package default voluta_checkpoints
        modelBuilder.Entity<CheckpointRecord>()
            .ToTable(ChatDatabase.Checkpoints, ChatDatabase.Schema);

        // The object axis, as row-level filters: a session's project is the
        // axis value (nullable: null means cross-project — visible to every
        // subject); a message inherits its session's project axis through
        // the navigation. Out-of-scope reads surface as not-found
        // downstream, never as a deny.
        modelBuilder.Entity<ChatSession>()
            .HasQueryFilter(session => ScopeUnrestricted
                || session.ProjectId == null
                || ScopeProjectIds.Contains(session.ProjectId.Value));
        modelBuilder.Entity<ChatMessage>()
            .HasQueryFilter(message => ScopeUnrestricted
                || Sessions.Any(session => session.Id == message.SessionId
                    && (session.ProjectId == null || ScopeProjectIds.Contains(session.ProjectId.Value))));

        base.OnModelCreating(modelBuilder);
    }
}
