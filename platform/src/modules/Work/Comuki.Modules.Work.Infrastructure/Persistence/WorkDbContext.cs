using Comuki.Modules.Work.Infrastructure.Persistence.Configurations;
using Comuki.Modules.Work.Infrastructure.Persistence.Entities;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Modules.Work.Infrastructure.Persistence;

/// <summary>
/// EF model for the Work schema: <c>work_tasks</c> +
/// <c>work_task_attempts</c> + side tables +
/// <c>inbox_receipts</c> + <c>outbox_watermarks</c>. Snake_case
/// naming is applied by the shared options helper
/// (<see cref="ApplyOptions"/>) via
/// <c>UseSnakeCaseNamingConvention</c>; column names are still
/// written explicitly in the configurations so migration snapshots
/// stay stable. The migrations history table lives in the Work
/// schema at <c>work.__ef_migrations_history</c> (the standard EF
/// Core Npgsql convention) so all contexts migrate one database
/// without colliding. The Work scope is project-scoped —
/// <c>WorkTask</c> rows surface through the global subject-scope
/// query filter, the same way other modules' aggregates do.
/// </summary>
public sealed class WorkDbContext(
    DbContextOptions<WorkDbContext> options,
    ISubjectScopeAccessor? scopeAccessor = null)
    : DbContext(options)
{
    /// <summary>WorkTask aggregate roots.</summary>
    public DbSet<WorkTaskEntity> WorkTasks => Set<WorkTaskEntity>();

    /// <summary>Append-only attempt ledger (one row per AppendAttempt call).</summary>
    public DbSet<WorkTaskAttemptEntity> WorkTaskAttempts => Set<WorkTaskAttemptEntity>();

    /// <summary>Source references (one per external-system link, one primary per task).</summary>
    public DbSet<WorkTaskSourceRefEntity> WorkTaskSourceRefs => Set<WorkTaskSourceRefEntity>();

    /// <summary>Outgoing dependency edges (blocks / relates-to).</summary>
    public DbSet<WorkTaskDependencyEntity> WorkTaskDependencies => Set<WorkTaskDependencyEntity>();

    /// <summary>Responsible-actor assignments.</summary>
    public DbSet<WorkTaskAssignmentEntity> WorkTaskAssignments => Set<WorkTaskAssignmentEntity>();

    /// <summary>Per-Task completion policy rows (versioned).</summary>
    public DbSet<WorkTaskCompletionPolicyEntity> WorkTaskCompletionPolicies => Set<WorkTaskCompletionPolicyEntity>();

    /// <summary>Mirror inbox dedupe ledger (per-task message id PK).</summary>
    public DbSet<InboxReceiptEntity> InboxReceipts => Set<InboxReceiptEntity>();

    /// <summary>Per-subscriber outbox-poll watermark (engine <c>outbox_messages.id</c> snapshot).</summary>
    public DbSet<OutboxWatermarkEntity> OutboxWatermarks => Set<OutboxWatermarkEntity>();

    /// <summary>Left disjunct of the scope filter: true when the current subject sees every project.</summary>
    public bool ScopeUnrestricted => scopeAccessor?.Current.Unrestricted ?? true;

    /// <summary>Projects the current subject is confined to; empty means "no project", not "any project".</summary>
    public ProjectId[] ScopeProjectIds => scopeAccessor is { } accessor
        ? [.. accessor.Current.ProjectIds]
        : [];

    /// <summary>
    /// Single options recipe (Npgsql + snake_case + per-schema history
    /// table) used by the DI extension, the design-time factory and
    /// the Migrator — one place, no drift. The migrations history
    /// table lives in the work schema at
    /// <c>work.__ef_migrations_history</c>.
    /// </summary>
    public static void ApplyOptions(DbContextOptionsBuilder builder, string connectionString)
    {
        builder
            .UseNpgsql(connectionString, static npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", WorkDatabase.Schema))
            .UseSnakeCaseNamingConvention();
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .ApplyConfiguration(new WorkTaskConfiguration())
            .ApplyConfiguration(new WorkTaskAttemptConfiguration())
            .ApplyConfiguration(new WorkTaskSourceRefConfiguration())
            .ApplyConfiguration(new WorkTaskDependencyConfiguration())
            .ApplyConfiguration(new WorkTaskAssignmentConfiguration())
            .ApplyConfiguration(new WorkTaskCompletionPolicyConfiguration())
            .ApplyConfiguration(new InboxReceiptConfiguration())
            .ApplyConfiguration(new OutboxWatermarkConfiguration());

        // Object axis as a row-level filter: a Task is visible when its
        // project is in the subject's scope; the side tables follow the
        // same project as the parent Task. Out-of-scope reads surface as
        // not-found downstream, never as a deny.
        modelBuilder.Entity<WorkTaskEntity>()
            .HasQueryFilter(task => ScopeUnrestricted
                || ScopeProjectIds.Any(p => p.Value == task.ProjectId));
        modelBuilder.Entity<WorkTaskAttemptEntity>()
            .HasQueryFilter(attempt => ScopeUnrestricted || WorkTasks.Any(task => task.Id == attempt.TaskId));
        modelBuilder.Entity<WorkTaskSourceRefEntity>()
            .HasQueryFilter(source => ScopeUnrestricted || WorkTasks.Any(task => task.Id == source.TaskId));
        modelBuilder.Entity<WorkTaskDependencyEntity>()
            .HasQueryFilter(edge => ScopeUnrestricted || WorkTasks.Any(task => task.Id == edge.TaskId));
        modelBuilder.Entity<WorkTaskAssignmentEntity>()
            .HasQueryFilter(assignment => ScopeUnrestricted || WorkTasks.Any(task => task.Id == assignment.TaskId));
        modelBuilder.Entity<WorkTaskCompletionPolicyEntity>()
            .HasQueryFilter(policy => ScopeUnrestricted || WorkTasks.Any(task => task.Id == policy.TaskId));

        base.OnModelCreating(modelBuilder);
    }
}
