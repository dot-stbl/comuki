using Comuki.Modules.Costs.Domain.Events;
using Comuki.Modules.Costs.Infrastructure.Persistence.Configurations;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Modules.Costs.Infrastructure.Persistence;

/// <summary>
/// EF model for the Costs schema: <c>usage_events</c>. Snake_case naming
/// is applied by the shared options recipe (<see cref="ApplyOptions"/>);
/// column names are still written explicitly in the configurations so
/// migration snapshots stay stable. Every entity carries the global
/// subject-scope query filter — the object axis of the authorization
/// model (out-of-scope rows surface as 404 downstream, never as a deny).
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
public sealed class CostsDbContext(
    DbContextOptions<CostsDbContext> options,
    ISubjectScopeAccessor? scopeAccessor = null)
    : DbContext(options)
{
    /// <summary>Metered usage events.</summary>
    public DbSet<UsageEvent> UsageEvents => Set<UsageEvent>();

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
    /// Single options recipe (Npgsql + snake_case + private history table)
    /// used by the DI extension, the design-time factory and the Migrator.
    /// </summary>
    /// <param name="builder"></param>
    /// <param name="connectionString"></param>
    public static void ApplyOptions(DbContextOptionsBuilder builder, string connectionString)
    {
        builder
            .UseNpgsql(connectionString, static npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", CostsDatabase.Schema))
            .UseSnakeCaseNamingConvention();
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new UsageEventConfiguration());

        // The object axis, as a row-level filter: a usage event is
        // visible when its project is in the subject's scope.
        modelBuilder.Entity<UsageEvent>()
            .HasQueryFilter(usageEvent => ScopeUnrestricted || ScopeProjectIds.Contains(usageEvent.ProjectId));

        base.OnModelCreating(modelBuilder);
    }
}
