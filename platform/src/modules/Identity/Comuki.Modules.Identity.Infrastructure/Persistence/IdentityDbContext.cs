using Comuki.Modules.Identity.Domain.ApiKeys;
using Comuki.Modules.Identity.Domain.Assignments;
using Comuki.Modules.Identity.Domain.Users;
using Comuki.Modules.Identity.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// EF model for the Identity schema: users / api_keys / role_assignments /
/// oidc_links. Snake_case naming is applied by the shared options recipe
/// (<see cref="ApplyOptions"/>) via <c>UseSnakeCaseNamingConvention</c>;
/// column names are still written explicitly in the configurations so
/// migration snapshots stay stable. The migrations history table is
/// module-private (<see cref="IdentityTables.MigrationsHistory"/>) so this
/// context and the orchestration context can migrate the same database.
/// </summary>
/// <param name="options"></param>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options)
    : DbContext(options)
{
    /// <summary>User accounts — aggregate roots.</summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>API keys.</summary>
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    /// <summary>Role assignments.</summary>
    public DbSet<RoleAssignment> RoleAssignments => Set<RoleAssignment>();

    /// <summary>OIDC identity links.</summary>
    public DbSet<OidcLink> OidcLinks => Set<OidcLink>();

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
            .UseNpgsql(connectionString, static npgsql => _ = npgsql.MigrationsHistoryTable(IdentityTables.MigrationsHistory))
            .UseSnakeCaseNamingConvention();
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .ApplyConfiguration(new UserConfiguration())
            .ApplyConfiguration(new ApiKeyConfiguration())
            .ApplyConfiguration(new RoleAssignmentConfiguration())
            .ApplyConfiguration(new OidcLinkConfiguration());
        base.OnModelCreating(modelBuilder);
    }
}
