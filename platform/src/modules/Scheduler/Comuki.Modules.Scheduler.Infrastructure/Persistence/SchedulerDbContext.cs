using Comuki.Modules.Scheduler.Domain.Jobs;
using Comuki.Modules.Scheduler.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Modules.Scheduler.Infrastructure.Persistence;

/// <summary>
/// EF model for the Scheduler schema: scheduled_jobs. Snake_case naming
/// is applied by the shared options recipe (<see cref="ApplyOptions"/>)
/// via <c>UseSnakeCaseNamingConvention</c>; column names are still
/// written explicitly in the configuration so migration snapshots stay
/// stable. The migrations history table lives in the scheduler schema at
/// <c>scheduler.__ef_migrations_history</c> so all contexts migrate one
/// database without colliding.
/// </summary>
/// <param name="options"></param>
public sealed class SchedulerDbContext(DbContextOptions<SchedulerDbContext> options)
    : DbContext(options)
{
    /// <summary>Scheduled jobs of every project.</summary>
    public DbSet<ScheduledJob> ScheduledJobs => Set<ScheduledJob>();

    /// <summary>
    /// Single options recipe (Npgsql + snake_case + private history
    /// table) used by the DI extension, the design-time factory and the
    /// Migrator — one place, no drift.
    /// </summary>
    /// <param name="builder"></param>
    /// <param name="connectionString"></param>
    public static void ApplyOptions(DbContextOptionsBuilder builder, string connectionString)
    {
        builder
            .UseNpgsql(connectionString, static npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", SchedulerDatabase.Schema))
            .UseSnakeCaseNamingConvention();
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .ApplyConfiguration(new ScheduledJobConfiguration());
        base.OnModelCreating(modelBuilder);
    }
}
