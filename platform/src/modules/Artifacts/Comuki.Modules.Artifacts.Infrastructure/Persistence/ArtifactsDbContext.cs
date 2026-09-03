using Comuki.Modules.Artifacts.Domain;
using Comuki.Modules.Artifacts.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Modules.Artifacts.Infrastructure.Persistence;

/// <summary>
/// EF model for the artifacts schema: <c>run_bundles</c> — one row per
/// run whose artifacts have been packaged to the artifact store. Snake_case
/// naming is applied by the shared options recipe
/// (<see cref="ApplyOptions"/>) via <c>UseSnakeCaseNamingConvention</c>;
/// column names are still written explicitly in the configurations so
/// migration snapshots stay stable.
/// </summary>
/// <param name="options"></param>
public sealed class ArtifactsDbContext(DbContextOptions<ArtifactsDbContext> options)
    : DbContext(options)
{
    /// <summary>One row per packaged run.</summary>
    public DbSet<RunArtifactBundle> RunBundles => Set<RunArtifactBundle>();

    /// <summary>
    /// Single options recipe (Npgsql + snake_case + private history table
    /// in the <c>artifacts</c> schema) used by the DI extension, the
    /// design-time factory and the Migrator.
    /// </summary>
    /// <param name="builder"></param>
    /// <param name="connectionString"></param>
    public static void ApplyOptions(DbContextOptionsBuilder builder, string connectionString)
    {
        builder
            .UseNpgsql(connectionString, static npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", ArtifactsDatabase.Schema))
            .UseSnakeCaseNamingConvention();
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new RunBundleConfiguration());
        base.OnModelCreating(modelBuilder);
    }
}
