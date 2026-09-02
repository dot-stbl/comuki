using Comuki.Engine.Orchestration.Domain.Journal;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Domain.WorkItems;
using Comuki.Engine.Orchestration.Infrastructure.Persistence.Configurations;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Engine.Orchestration.Infrastructure.Persistence;

/// <summary>
/// EF model for the orchestration schema: runs / work_items /
/// work_item_dependencies / run_events. Snake_case naming is applied by the
/// shared options helper (<see cref="ApplyOptions"/>) via
/// <c>UseSnakeCaseNamingConvention</c>; column names are still written
/// explicitly in the configurations so migration snapshots stay stable.
/// Runs and work items carry the global subject-scope query filter — the
/// object axis of the authorization model (out-of-scope rows surface as
/// 404 downstream, never as a deny).
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
public sealed class OrchestrationDbContext(
    DbContextOptions<OrchestrationDbContext> options,
    ISubjectScopeAccessor? scopeAccessor = null)
    : DbContext(options)
{
    /// <summary>Runs — aggregate roots.</summary>
    public DbSet<Run> Runs => Set<Run>();

    /// <summary>Work items — the queue.</summary>
    public DbSet<WorkItem> WorkItems => Set<WorkItem>();

    /// <summary>Plan DAG edges.</summary>
    public DbSet<WorkItemDependency> WorkItemDependencies => Set<WorkItemDependency>();

    /// <summary>Append-only run journal.</summary>
    public DbSet<RunEvent> RunEvents => Set<RunEvent>();

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
    /// Single options recipe (Npgsql + snake_case) used by the DI extension,
    /// the design-time factory and the Migrator — one place, no drift. Takes
    /// the non-generic builder so the <c>AddDbContext</c> lambda can call it
    /// directly; generic builders pass through unchanged (same instance).
    /// </summary>
    /// <param name="builder"></param>
    /// <param name="connectionString"></param>
    public static void ApplyOptions(DbContextOptionsBuilder builder, string connectionString)
    {
        _ = builder
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention();
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        _ = modelBuilder
            .ApplyConfiguration(new RunConfiguration())
            .ApplyConfiguration(new WorkItemConfiguration())
            .ApplyConfiguration(new WorkItemDependencyConfiguration())
            .ApplyConfiguration(new RunEventConfiguration());

        // The object axis, as row-level filters: a run is visible when its
        // project is in the subject's scope; a work item (no project column
        // of its own) is visible when its parent run is. The filter shape is
        // constant and reads the scope off THIS context instance, so EF
        // parameterises it per query and the cached model is shared.
        _ = modelBuilder.Entity<Run>()
            .HasQueryFilter(run => ScopeUnrestricted || ScopeProjectIds.Contains(run.ProjectId));
        _ = modelBuilder.Entity<WorkItem>()
            .HasQueryFilter(item => ScopeUnrestricted || Runs.Any(run => run.Id == item.RunId));

        base.OnModelCreating(modelBuilder);
    }
}
