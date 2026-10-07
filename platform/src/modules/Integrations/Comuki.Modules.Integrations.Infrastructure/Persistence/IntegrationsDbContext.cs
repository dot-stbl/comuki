using Comuki.Modules.Integrations.Domain.Connections;
using Comuki.Modules.Integrations.Domain.Deliveries;
using Comuki.Modules.Integrations.Domain.Items;
using Comuki.Modules.Integrations.Domain.Rules;
using Comuki.Modules.Integrations.Domain.Sync;
using Comuki.Modules.Integrations.Infrastructure.Persistence.Configurations;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Modules.Integrations.Infrastructure.Persistence;

/// <summary>
/// EF model for the Integrations schema: inbound_items / source_connections /
/// admission_rules. Snake_case naming is applied by the shared options
/// recipe (<see cref="ApplyOptions"/>) via <c>UseSnakeCaseNamingConvention</c>;
/// column names are still written explicitly in the configurations so
/// migration snapshots stay stable. The migrations history table lives
/// in the integrations schema at <c>integrations.n_history</c> (domain
/// convention <c>&lt;schema&gt;.n_history</c>) so all contexts migrate one
/// database without colliding. Every owned entity carries the global
/// subject-scope query filter — the object axis of the authorization
/// model (out-of-scope rows surface as 404 downstream, never as a deny).
/// <see cref="Delivery"/> and <see cref="Sync"/> follow their parent
/// inbound item — both project the inherited project id through the
/// navigation filter.
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
public sealed class IntegrationsDbContext(
    DbContextOptions<IntegrationsDbContext> options,
    ISubjectScopeAccessor? scopeAccessor = null)
    : DbContext(options)
{
    /// <summary>Seen external issues.</summary>
    public DbSet<InboundItem> Tickets => Set<InboundItem>();

    /// <summary>Webhook deliveries (insert-first idempotency).</summary>
    public DbSet<Delivery> Deliveries => Set<Delivery>();

    /// <summary>Tracker bindings.</summary>
    public DbSet<SourceConnection> Connections => Set<SourceConnection>();

    /// <summary>Admission rules.</summary>
    public DbSet<AdmissionRule> Rules => Set<AdmissionRule>();

    /// <summary>Sync-back outbox.</summary>
    public DbSet<SyncJob> SyncJobs => Set<SyncJob>();

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
            .UseNpgsql(connectionString, static npgsql => npgsql.MigrationsHistoryTable("n_history", IntegrationsDatabase.Schema))
            .UseSnakeCaseNamingConvention();
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .ApplyConfiguration(new InboundItemConfiguration())
            .ApplyConfiguration(new DeliveryConfiguration())
            .ApplyConfiguration(new SourceConnectionConfiguration())
            .ApplyConfiguration(new AdmissionRuleConfiguration())
            .ApplyConfiguration(new SyncJobConfiguration());

        // The object axis, as row-level filters: a project's own identity
        // is the axis value; a settings row and an admission policy follow
        // their project. Out-of-scope reads surface as not-found
        // downstream, never as a deny.
        modelBuilder.Entity<InboundItem>()
            .HasQueryFilter(ticket => ScopeUnrestricted || ScopeProjectIds.Contains(ticket.ProjectId));
        modelBuilder.Entity<SourceConnection>()
            .HasQueryFilter(connection => ScopeUnrestricted || ScopeProjectIds.Contains(connection.ProjectId));
        modelBuilder.Entity<AdmissionRule>()
            .HasQueryFilter(rule => ScopeUnrestricted || ScopeProjectIds.Contains(rule.ProjectId));

        base.OnModelCreating(modelBuilder);
    }
}
