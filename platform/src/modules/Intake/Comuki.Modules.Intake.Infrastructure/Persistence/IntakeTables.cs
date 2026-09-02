namespace Comuki.Modules.Intake.Infrastructure.Persistence;

/// <summary>
/// Physical Intake table names — the single source every EF
/// configuration reads; no magic strings in
/// <c>IEntityTypeConfiguration</c>. The module keeps its own migrations
/// history (the 6th context of one database: orchestration, identity,
/// projects, memory, chat, intake).
/// </summary>
public static class IntakeTables
{
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

    /// <summary>Module-private EF migrations history table.</summary>
    public const string MigrationsHistory = "__comuki_intake";
}
