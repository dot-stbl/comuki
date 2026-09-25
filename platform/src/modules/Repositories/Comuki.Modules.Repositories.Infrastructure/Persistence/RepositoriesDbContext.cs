using Microsoft.EntityFrameworkCore;

namespace Comuki.Modules.Repositories.Infrastructure.Persistence;

/// <summary>
/// EF model for the repositories schema. Empty until workstream 2's
/// aggregates land — this skeleton creates the migration history table
/// so the Migrator exe and host boot-time auto-migrate have a
/// well-known anchor in the database. Snake_case naming is applied by
/// the shared options recipe (<see cref="ApplyOptions"/>) via
/// <c>UseSnakeCaseNamingConvention</c>; column names will be written
/// explicitly in the configurations when they arrive. The migrations
/// history table lives in the repositories schema at
/// <c>repositories.__comuki_repositories</c> — named differently from
/// sibling modules' <c>__ef_migrations_history</c> tables on purpose:
/// each module keeps its own per-schema history so all module contexts
/// migrate one database without colliding.
/// </summary>
/// <param name="options"></param>
public sealed class RepositoriesDbContext(DbContextOptions<RepositoriesDbContext> options)
    : DbContext(options)
{
    /// <summary>
    /// Single options recipe (Npgsql + snake_case + private history
    /// table) used by the DI extension, the design-time factory and the
    /// Migrator.
    /// </summary>
    /// <param name="builder"></param>
    /// <param name="connectionString"></param>
    public static void ApplyOptions(DbContextOptionsBuilder builder, string connectionString)
    {
        builder
            .UseNpgsql(connectionString, static npgsql => npgsql.MigrationsHistoryTable("__comuki_repositories", RepositoriesDatabase.Schema))
            .UseSnakeCaseNamingConvention();
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configurations arrive with workstream 2 (Repository aggregate
        // and its tables); the base call must remain so EF Core registers
        // its own conventions.
        base.OnModelCreating(modelBuilder);
    }
}
