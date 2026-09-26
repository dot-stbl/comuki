using Comuki.Modules.Repositories.Domain.Repositories;
using Comuki.Modules.Repositories.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Modules.Repositories.Infrastructure.Persistence;

/// <summary>
/// EF model for the repositories schema — the three aggregates that the
/// registry tracks: <see cref="Repository"/> itself, its rule snapshot
/// <see cref="RepositoryPolicy"/>, and its credential reference
/// <see cref="RepositoryCredentialRef"/>. Snake_case naming is applied by
/// the shared options recipe (<see cref="ApplyOptions"/>) via
/// <c>UseSnakeCaseNamingConvention</c>; column names are written explicitly
/// in each <c>IEntityTypeConfiguration</c>. The migrations history table
/// lives in the repositories schema at
/// <c>repositories.__comuki_repositories</c> — named differently from
/// sibling modules' <c>__ef_migrations_history</c> tables on purpose:
/// each module keeps its own per-schema history so all module contexts
/// migrate one database without colliding.
/// </summary>
/// <param name="options"></param>
public sealed class RepositoriesDbContext(DbContextOptions<RepositoriesDbContext> options)
    : DbContext(options)
{
    /// <summary>Registered repositories (the dedup-keyed <c>repositories</c> table).</summary>
    public DbSet<Repository> Repositories => Set<Repository>();

    /// <summary>Per-Repository rule snapshot (<c>repository_policies</c>, FK-cascade to <c>repositories</c>).</summary>
    public DbSet<RepositoryPolicy> RepositoryPolicies => Set<RepositoryPolicy>();

    /// <summary>Per-Repository credential reference (<c>repository_credential_refs</c>, FK-cascade).</summary>
    public DbSet<RepositoryCredentialRef> RepositoryCredentialRefs => Set<RepositoryCredentialRef>();

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
        modelBuilder
            .ApplyConfiguration(new RepositoryConfiguration())
            .ApplyConfiguration(new RepositoryPolicyConfiguration())
            .ApplyConfiguration(new RepositoryCredentialRefConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
