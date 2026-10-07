namespace Comuki.Modules.Integrations.Infrastructure.Persistence;

/// <summary>
/// Physical Integrations database — the Postgres schema name plus every
/// table that belongs to it. Single source every
/// <c>IEntityTypeConfiguration</c> reads; no magic strings in
/// <c>builder.ToTable(...)</c>. The migration history table lives at
/// <c>integrations.n_history</c> (domain convention
/// <c>&lt;schema&gt;.n_history</c>) and is configured via
/// <c>npgsql.MigrationsHistoryTable(name, schema)</c> in
/// <see cref="IntegrationsDbContext.ApplyOptions"/>.
/// </summary>
public static class IntegrationsDatabase
{
    /// <summary>Postgres schema name.</summary>
    public const string Schema = "integrations";

    /// <summary>Seen external issues — the dedupe view and the active-run lock.</summary>
    public const string Tickets = "inbound_items";

    /// <summary>Webhook deliveries — the insert-first idempotency lock.</summary>
    public const string Deliveries = "deliveries";

    /// <summary>Tracker bindings (settings + env-ref secrets + webhook key).</summary>
    public const string Connections = "source_connections";

    /// <summary>Per-project admission rules (watch / inbox + filter).</summary>
    public const string Rules = "admission_rules";

    /// <summary>Sync-back outbox (status transitions pushed to trackers).</summary>
    public const string SyncJobs = "sync_jobs";
}
