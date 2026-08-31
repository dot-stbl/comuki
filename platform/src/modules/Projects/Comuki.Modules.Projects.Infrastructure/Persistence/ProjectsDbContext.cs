using Comuki.Modules.Projects.Domain.Projects;
using Comuki.Modules.Projects.Domain.Settings;
using Comuki.Modules.Projects.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Modules.Projects.Infrastructure.Persistence;

/// <summary>
/// EF model for the Projects schema: projects / project_settings.
/// Snake_case naming is applied by the shared options recipe
/// (<see cref="ApplyOptions"/>) via <c>UseSnakeCaseNamingConvention</c>;
/// column names are still written explicitly in the configurations so
/// migration snapshots stay stable. The migrations history table is
/// module-private (<see cref="ProjectsTables.MigrationsHistory"/>) so this
/// context, the orchestration context and the identity context can migrate
/// the same database.
/// </summary>
/// <param name="options"></param>
public sealed class ProjectsDbContext(DbContextOptions<ProjectsDbContext> options)
    : DbContext(options)
{
    /// <summary>Projects — aggregate roots.</summary>
    public DbSet<Project> Projects => Set<Project>();

    /// <summary>Per-project settings (one row per project).</summary>
    public DbSet<ProjectSettings> ProjectSettings => Set<ProjectSettings>();

    /// <summary>
    /// Single options recipe (Npgsql + snake_case + private history
    /// table) used by the DI extension, the design-time factory and the
    /// Migrator — one place, no drift.
    /// </summary>
    /// <param name="builder"></param>
    /// <param name="connectionString"></param>
    public static void ApplyOptions(DbContextOptionsBuilder builder, string connectionString)
    {
        _ = builder
            .UseNpgsql(connectionString, static npgsql => _ = npgsql.MigrationsHistoryTable(ProjectsTables.MigrationsHistory))
            .UseSnakeCaseNamingConvention();
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        _ = modelBuilder
            .ApplyConfiguration(new ProjectConfiguration())
            .ApplyConfiguration(new ProjectSettingsConfiguration());
        base.OnModelCreating(modelBuilder);
    }
}
