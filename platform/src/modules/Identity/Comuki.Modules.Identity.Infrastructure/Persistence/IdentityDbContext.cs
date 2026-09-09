using Comuki.Modules.Identity.Domain.ApiKeys;
using Comuki.Modules.Identity.Domain.Assignments;
using Comuki.Modules.Identity.Domain.Oidc;
using Comuki.Modules.Identity.Domain.Users;
using Comuki.Modules.Identity.Infrastructure.Persistence.Configurations;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// EF model for the Identity schema: users / api_keys / role_assignments /
/// oidc_links / oidc_states. Snake_case naming is applied by the shared
/// options recipe (<see cref="ApplyOptions"/>) via
/// <c>UseSnakeCaseNamingConvention</c>; column names are still written
/// explicitly in the configurations so migration snapshots stay stable.
/// The migrations history table lives in the identity schema at
/// <c>identity.__ef_migrations_history</c> so this context and the
/// orchestration context can migrate the same database without
/// colliding. Project-scoped entities (<see cref="ApiKey"/> via
/// <see cref="ApiKey.TenantProjectId"/>, <see cref="RoleAssignment"/> via
/// <see cref="RoleAssignment.ScopeProjectId"/>) carry the global
/// subject-scope query filter — the object axis of the authorization
/// model. A null project on either column means the row is platform-wide
/// (visible to every subject). Cross-project entities (Users,
/// OidcLink, OidcState) stay unfiltered — they're not project-owned.
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
public sealed class IdentityDbContext(
    DbContextOptions<IdentityDbContext> options,
    ISubjectScopeAccessor? scopeAccessor = null)
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

    /// <summary>In-flight OIDC authorization states.</summary>
    public DbSet<OidcState> OidcStates => Set<OidcState>();

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
    /// Migrator.
    /// </summary>
    /// <param name="builder"></param>
    /// <param name="connectionString"></param>
    public static void ApplyOptions(DbContextOptionsBuilder builder, string connectionString)
    {
        builder
            .UseNpgsql(connectionString, static npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", IdentityDatabase.Schema))
            .UseSnakeCaseNamingConvention();
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .ApplyConfiguration(new UserConfiguration())
            .ApplyConfiguration(new ApiKeyConfiguration())
            .ApplyConfiguration(new RoleAssignmentConfiguration())
            .ApplyConfiguration(new OidcLinkConfiguration())
            .ApplyConfiguration(new OidcStateConfiguration());

        // The object axis, as row-level filters: a null
        // TenantProjectId / ScopeProjectId means platform-wide (visible
        // to every subject); otherwise the row follows its project axis.
        // ProjectId is a nullable ProjectId type on both columns.
        modelBuilder.Entity<ApiKey>()
            .HasQueryFilter(apiKey => ScopeUnrestricted
                || apiKey.TenantProjectId == null
                || ScopeProjectIds.Contains(apiKey.TenantProjectId.Value));
        modelBuilder.Entity<RoleAssignment>()
            .HasQueryFilter(assignment => ScopeUnrestricted
                || assignment.ScopeProjectId == null
                || ScopeProjectIds.Contains(assignment.ScopeProjectId.Value));

        base.OnModelCreating(modelBuilder);
    }
}
