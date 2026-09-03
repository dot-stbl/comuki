namespace Comuki.Modules.Intake.Infrastructure.Persistence;

/// <summary>
/// Physical Intake database — the Postgres schema name plus every table that
/// belongs to it. Single source every <c>IEntityTypeConfiguration</c> reads;
/// no magic strings in <c>builder.ToTable(...)</c>. The migration history
/// table lives at <c>intake.__ef_migrations_history</c> (per the EF Core
/// Postgres convention) and is configured via
/// <c>npgsql.MigrationsHistoryTable(name, schema)</c> in
/// <see cref="IntakeDbContext.ApplyOptions"/>.
/// </summary>
public static class IntakeDatabase
{
    /// <summary>Postgres schema name. the namespace.</summary>
    public const string Schema = "intake";

    /// <summary>Seen external issues — the dedupe view and the active-run lock.</summary>
    public const string Tickets = "intake_tickets";

    /// <summary>Webhook deliveries — the insert-first idempotency lock.</summary>
    public const string Deliveries = "intake_deliveries";

    /// <summary>Tracker bindings (settings + env-ref secrets + webhook key).</summary>
    public const string Connections = "source_connections";

    /// <summary>Per-project admission rules (watch / inbox + filter).</summary>
    public const string Rules = "admission_rules";

    /// <summary>Sync-back outbox (status transitions pushed to trackers).</summary>
    public const string SyncJobs = "sync_jobs";
}