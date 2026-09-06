namespace Comuki.Modules.Scheduler.Infrastructure.Persistence;

/// <summary>
/// Physical Scheduler database — the Postgres schema name plus the
/// single table the module owns. Single source every
/// <c>IEntityTypeConfiguration</c> reads; no magic strings in
/// <c>builder.ToTable(...)</c>. The migration history table lives at
/// <c>scheduler.__ef_migrations_history</c> (per the EF Core Postgres
/// convention) and is configured via
/// <c>npgsql.MigrationsHistoryTable(name, schema)</c> in
/// <see cref="SchedulerDbContext.ApplyOptions"/>.
/// </summary>
public static class SchedulerDatabase
{
    /// <summary>Postgres schema name.</summary>
    public const string Schema = "scheduler";

    /// <summary>Scheduled jobs — the only table this module owns.</summary>
    public const string ScheduledJobs = "scheduled_jobs";
}
