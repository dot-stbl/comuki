using Comuki.Modules.Intake.Domain.Connections;
using Comuki.Modules.Intake.Domain.Deliveries;
using Comuki.Modules.Intake.Domain.Rules;
using Comuki.Modules.Intake.Domain.Sync;
using Comuki.Modules.Intake.Domain.Tickets;
using Comuki.Modules.Intake.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Modules.Intake.Infrastructure.Persistence;

/// <summary>
/// EF model for the Intake schema: intake_tickets / intake_deliveries /
/// source_connections / admission_rules / sync_jobs. Snake_case naming
/// is applied by the shared options recipe (<see cref="ApplyOptions"/>)
/// via <c>UseSnakeCaseNamingConvention</c>; column names are still
/// written explicitly in the configurations so migration snapshots stay
/// stable. The migrations history table lives in the intake schema at
/// <c>intake.__ef_migrations_history</c> so all contexts migrate one
/// database without colliding.
/// </summary>
/// <param name="options"></param>
public sealed class IntakeDbContext(DbContextOptions<IntakeDbContext> options)
    : DbContext(options)
{
    /// <summary>Seen external issues.</summary>
    public DbSet<IncomingTicket> Tickets => Set<IncomingTicket>();

    /// <summary>Webhook deliveries (insert-first idempotency).</summary>
    public DbSet<IntakeDelivery> Deliveries => Set<IntakeDelivery>();

    /// <summary>Tracker bindings.</summary>
    public DbSet<SourceConnection> Connections => Set<SourceConnection>();

    /// <summary>Admission rules.</summary>
    public DbSet<AdmissionRule> Rules => Set<AdmissionRule>();

    /// <summary>Sync-back outbox.</summary>
    public DbSet<SyncJob> SyncJobs => Set<SyncJob>();

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
            .UseNpgsql(connectionString, static npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", IntakeDatabase.Schema))
            .UseSnakeCaseNamingConvention();
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .ApplyConfiguration(new IncomingTicketConfiguration())
            .ApplyConfiguration(new IntakeDeliveryConfiguration())
            .ApplyConfiguration(new SourceConnectionConfiguration())
            .ApplyConfiguration(new AdmissionRuleConfiguration())
            .ApplyConfiguration(new SyncJobConfiguration());
        base.OnModelCreating(modelBuilder);
    }
}
