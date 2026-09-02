using Comuki.Modules.Costs.Domain.Events;
using Comuki.Modules.Costs.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Modules.Costs.Infrastructure.Persistence;

/// <summary>
/// EF model for the Costs schema: <c>usage_events</c>. Snake_case naming
/// is applied by the shared options recipe (<see cref="ApplyOptions"/>);
/// column names are still written explicitly in the configurations so
/// migration snapshots stay stable.
/// </summary>
/// <param name="options"></param>
public sealed class CostsDbContext(DbContextOptions<CostsDbContext> options)
    : DbContext(options)
{
    /// <summary>Metered usage events.</summary>
    public DbSet<UsageEvent> UsageEvents => Set<UsageEvent>();

    /// <summary>
    /// Single options recipe (Npgsql + snake_case + private history table)
    /// used by the DI extension, the design-time factory and the Migrator.
    /// </summary>
    /// <param name="builder"></param>
    /// <param name="connectionString"></param>
    public static void ApplyOptions(DbContextOptionsBuilder builder, string connectionString)
    {
        builder
            .UseNpgsql(connectionString, static npgsql => _ = npgsql.MigrationsHistoryTable(CostsTables.MigrationsHistory))
            .UseSnakeCaseNamingConvention();
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new UsageEventConfiguration());
        base.OnModelCreating(modelBuilder);
    }
}
