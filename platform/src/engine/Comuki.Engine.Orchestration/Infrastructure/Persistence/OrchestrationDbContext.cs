using Comuki.Engine.Orchestration.Domain.Journal;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Domain.WorkItems;
using Comuki.Engine.Orchestration.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Engine.Orchestration.Infrastructure.Persistence;

/// <summary>
/// EF model for the orchestration schema: runs / work_items /
/// work_item_dependencies / run_events. Snake_case naming is applied by the
/// shared options helper (<see cref="ApplyOptions"/>) via
/// <c>UseSnakeCaseNamingConvention</c>; column names are still written
/// explicitly in the configurations so migration snapshots stay stable.
/// </summary>
/// <param name="options"></param>
public sealed class OrchestrationDbContext(DbContextOptions<OrchestrationDbContext> options)
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
        base.OnModelCreating(modelBuilder);
    }
}
