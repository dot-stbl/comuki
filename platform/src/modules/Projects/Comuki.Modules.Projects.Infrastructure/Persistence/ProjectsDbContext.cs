using Comuki.Modules.Projects.Domain.Projects;
using Comuki.Modules.Projects.Domain.Settings;
using Comuki.Modules.Projects.Infrastructure.Persistence.Configurations;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Modules.Projects.Infrastructure.Persistence;

/// <summary>
/// EF model for the Projects schema: projects / project_settings.
/// Snake_case naming is applied by the shared options recipe
/// (<see cref="ApplyOptions"/>) via <c>UseSnakeCaseNamingConvention</c>;
/// column names are still written explicitly in the configurations so
/// migration snapshots stay stable. The migrations history table lives in
/// the projects schema at <c>projects.__ef_migrations_history</c> so this
/// context, the orchestration context and the identity context can migrate
/// the same database without colliding. Both entity types carry the global
/// subject-scope query filter — the object axis of the authorization model.
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
public sealed class ProjectsDbContext(
    DbContextOptions<ProjectsDbContext> options,
    ISubjectScopeAccessor? scopeAccessor = null)
    : DbContext(options)
{
    /// <summary>Projects — aggregate roots.</summary>
    public DbSet<Project> Projects => Set<Project>();

    /// <summary>Per-project settings (one row per project).</summary>
    public DbSet<ProjectSettings> ProjectSettings => Set<ProjectSettings>();

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
    /// <param name="builder"></param>
    /// <param name="connectionString"></param>
    public static void ApplyOptions(DbContextOptionsBuilder builder, string connectionString)
    {
        builder
.UseNpgsql(connectionString, static npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", ProjectsDatabase.Schema))
            .UseSnakeCaseNamingConvention();
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .ApplyConfiguration(new ProjectConfiguration())
            .ApplyConfiguration(new ProjectSettingsConfiguration());

        // The object axis, as row-level filters: a project's own identity is
        // the axis value; a settings row follows its project. Out-of-scope
        // reads surface as not-found downstream, never as a deny.
        modelBuilder.Entity<Project>()
            .HasQueryFilter(project => ScopeUnrestricted || ScopeProjectIds.Contains(project.Id));
        modelBuilder.Entity<ProjectSettings>()
            .HasQueryFilter(settings => ScopeUnrestricted || ScopeProjectIds.Contains(settings.ProjectId));

        base.OnModelCreating(modelBuilder);
    }
}
