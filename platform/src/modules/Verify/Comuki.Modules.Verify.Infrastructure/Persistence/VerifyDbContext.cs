using Comuki.Modules.Verify.Domain.Runs;
using Comuki.Modules.Verify.Infrastructure.Persistence.Configurations;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Modules.Verify.Infrastructure.Persistence;

/// <summary>
/// EF model for the Verify schema: generic_command_runs. Snake_case
/// naming is applied by the shared options recipe
/// (<see cref="ApplyOptions"/>) via <c>UseSnakeCaseNamingConvention</c>;
/// column names are still written explicitly in the configuration so
/// migration snapshots stay stable. The migrations history table lives
/// in the verify schema at <c>verify.__ef_migrations_history</c> so all
/// contexts migrate one database without colliding. The entity carries
/// the global subject-scope query filter — the object axis of the
/// authorization model (out-of-scope rows surface as 404 downstream,
/// never as a deny).
/// </summary>
/// <param name="options">EF context options built by <see cref="ApplyOptions"/> (DI, the design-time factory, or the Migrator).</param>
/// <param name="scopeAccessor">
/// Ambient subject scope (singleton; state in <see cref="AsyncLocal{T}"/>).
/// Optional so direct construction — the Migrator, design-time factories,
/// test fixtures — keeps compiling; a context built without an accessor is
/// by definition a system consumer and sees everything. A context built
/// WITH one (the host DI) fails loudly on a flow that established no
/// scope: the worker declares <see cref="ISubjectScopeAccessor.AsSystem"/>,
/// request paths get their scope from the host middleware.
/// </param>
public sealed class VerifyDbContext(
    DbContextOptions<VerifyDbContext> options,
    ISubjectScopeAccessor? scopeAccessor = null)
    : DbContext(options)
{
    /// <summary>Generic-command verification runs.</summary>
    public DbSet<GenericCommandRun> GenericCommandRuns => Set<GenericCommandRun>();

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
    /// Single options recipe (Npgsql + snake_case + private history
    /// table) used by the DI extension, the design-time factory and the
    /// Migrator — one place, no drift.
    /// </summary>
    /// <param name="builder">Options builder to configure in place.</param>
    /// <param name="connectionString">Postgres connection string.</param>
    public static void ApplyOptions(DbContextOptionsBuilder builder, string connectionString)
    {
        builder
            .UseNpgsql(connectionString, static npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", VerifyDatabase.Schema))
            .UseSnakeCaseNamingConvention();
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new GenericCommandRunConfiguration());

        // The object axis, as a row-level filter: a global gate run
        // (ProjectId null) is visible only to an unrestricted subject —
        // it never "leaks into" a project-scoped subject's view just
        // because it has no project of its own. A project-scoped run is
        // visible when its project is in the subject's scope.
        //
        // The array is projected to ProjectId? (not compared via
        // run.ProjectId.Value against a plain ProjectId[]) so both sides
        // of Contains share the exact same nullable type the column's
        // value converter (ProjectIdToNullableUuid) targets — mixing
        // ProjectId and ProjectId? here makes the Npgsql array-parameter
        // translation pick the wrong converter overload and throw at
        // query time. Contains(null) is never true against this
        // projection (Select never yields null), so a global gate run
        // (ProjectId null) correctly falls through to ScopeUnrestricted
        // with no extra null guard needed.
        modelBuilder.Entity<GenericCommandRun>()
            .HasQueryFilter(run => ScopeUnrestricted
                || ScopeProjectIds.Select(static id => (ProjectId?)id).Contains(run.ProjectId));

        base.OnModelCreating(modelBuilder);
    }
}
