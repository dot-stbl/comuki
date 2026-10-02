using Comuki.Modules.Procedures.Infrastructure.Persistence.Configurations;
using Comuki.Modules.Procedures.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Modules.Procedures.Infrastructure.Persistence;

/// <summary>
/// EF model for the procedures schema. Snake_case naming is applied by
/// the shared options recipe (<see cref="ApplyOptions"/>) via
/// <c>UseSnakeCaseNamingConvention</c>; column names are written
/// explicitly in each <c>IEntityTypeConfiguration</c>.
/// </summary>
/// <param name="options">EF options.</param>
public sealed class ProceduresDbContext(DbContextOptions<ProceduresDbContext> options)
    : DbContext(options)
{
    /// <summary>Immutable compiled procedure versions (content-addressed).</summary>
    public DbSet<CompiledProcedureVersionEntity> CompiledProcedureVersions => Set<CompiledProcedureVersionEntity>();

    /// <summary>
    /// Single options recipe (Npgsql + snake_case + private history
    /// table) used by the DI extension, the design-time factory, and
    /// the Migrator's <c>MigrationTarget</c> entry for <c>procedures</c>.
    /// </summary>
    /// <param name="builder">Options builder.</param>
    /// <param name="connectionString">Postgres connection string.</param>
    public static void ApplyOptions(DbContextOptionsBuilder builder, string connectionString)
    {
        builder
            .UseNpgsql(
                connectionString,
                static npgsql => npgsql.MigrationsHistoryTable(
                    ProceduresDatabase.MigrationsHistoryTable,
                    ProceduresDatabase.Schema))
            .UseSnakeCaseNamingConvention();
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new CompiledProcedureVersionConfiguration());
        base.OnModelCreating(modelBuilder);
    }
}
