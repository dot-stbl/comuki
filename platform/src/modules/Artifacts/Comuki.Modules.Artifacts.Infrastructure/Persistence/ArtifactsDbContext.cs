using Comuki.Modules.Artifacts.Domain;
using Comuki.Modules.Artifacts.Domain.VisualArtifacts;
using Comuki.Modules.Artifacts.Infrastructure.Persistence.Configurations;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Modules.Artifacts.Infrastructure.Persistence;

/// <summary>
/// EF model for the artifacts schema: <c>run_bundles</c> — one row per
/// run whose artifacts have been packaged to the artifact store. Snake_case
/// naming is applied by the shared options recipe
/// (<see cref="ApplyOptions"/>) via <c>UseSnakeCaseNamingConvention</c>;
/// column names are still written explicitly in the configurations so
/// migration snapshots stay stable. Every entity carries the global
/// subject-scope query filter — the object axis of the authorization
/// model (out-of-scope rows surface as 404 downstream, never as a deny).
/// <see cref="RunArtifactBundle.ProjectId"/> is a denormalised
/// <see cref="Guid"/> (raw, not the <c>ProjectId</c> strongly-typed id)
/// because the row is read by an asynchronous packager that doesn't
/// have access to the strongly-typed contract; the filter projects the
/// ambient scope's <c>ProjectId.Value</c> for comparison.
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
public sealed class ArtifactsDbContext(
    DbContextOptions<ArtifactsDbContext> options,
    ISubjectScopeAccessor? scopeAccessor = null)
    : DbContext(options)
{
    /// <summary>One row per packaged run.</summary>
    public DbSet<RunArtifactBundle> RunBundles => Set<RunArtifactBundle>();

    /// <summary>One row per published visual artifact (issue #51 slice 1).</summary>
    public DbSet<VisualArtifact> VisualArtifacts => Set<VisualArtifact>();

    /// <summary>
    /// Left disjunct of the scope filter: true when the current subject
    /// sees every project (a platform-scope role, a system consumer, or a
    /// directly-constructed system context).
    /// </summary>
    public bool ScopeUnrestricted => scopeAccessor?.Current.Unrestricted ?? true;

    /// <summary>
    /// Projects the current subject is confined to, projected onto the
    /// raw <see cref="Guid"/> values EF Core can translate. Empty means
    /// "no project", not "any project". Re-materialised per read — a
    /// copy of the already-resolved scope, not a walk. The query
    /// filters below compare against this Guid array (not the strongly-
    /// typed <c>ProjectId</c>) because EF Core's query translator
    /// cannot lower a custom struct's member access into SQL.
    /// </summary>
    public Guid[] ScopeProjectIds => scopeAccessor is { } accessor
        ? [.. accessor.Current.ProjectIds.Select(static projectId => projectId.Value)]
        : [];

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
        modelBuilder.ApplyConfiguration(new VisualArtifactConfiguration());

        // The object axis, as a row-level filter: a bundle is visible when
        // its denormalised project matches one in the subject's scope.
        // ProjectId is a raw Guid on this entity; the filter compares
        // against ScopeProjectIds (Guid[]) so EF Core can translate it.
        modelBuilder.Entity<RunArtifactBundle>()
            .HasQueryFilter(bundle => ScopeUnrestricted
                || ScopeProjectIds.Contains(bundle.ProjectId));

        // Same scope rule for visual artifacts — out-of-scope rows
        // surface as 404 to the content proxy, never as a deny.
        modelBuilder.Entity<VisualArtifact>()
            .HasQueryFilter(artifact => ScopeUnrestricted
                || ScopeProjectIds.Contains(artifact.ProjectId));

        base.OnModelCreating(modelBuilder);
    }
}
