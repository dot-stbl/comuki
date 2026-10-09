namespace Comuki.Modules.Work.Infrastructure.Persistence;

/// <summary>
/// Physical Work database — the Postgres schema name plus every
/// table that belongs to it. Single source every
/// <c>IEntityTypeConfiguration</c> reads; no magic strings in
/// <c>builder.ToTable(...)</c>. The migration history table lives
/// at <c>work.__ef_migrations_history</c> and is configured via
/// <c>npgsql.MigrationsHistoryTable(name, schema)</c> in
/// <see cref="WorkDbContext.ApplyOptions"/>.
/// </summary>
public static class WorkDatabase
{
    /// <summary>Postgres schema name (unprefixed — post-#88 convention per <c>add-work-management/design.md</c> Open Question C).</summary>
    public const string Schema = "work";

    /// <summary>WorkTask aggregate roots.</summary>
    public const string WorkTasks = "work_tasks";

    /// <summary>Append-only attempt ledger (one row per <see cref="Domain.WorkTask.AppendAttempt"/> call).</summary>
    public const string WorkTaskAttempts = "work_task_attempts";

    /// <summary>Source references (one per external-system link, one primary per task).</summary>
    public const string WorkTaskSourceRefs = "work_task_source_refs";

    /// <summary>Outgoing dependency edges (blocks / relates-to).</summary>
    public const string WorkTaskDependencies = "work_task_dependencies";

    /// <summary>Responsible-actor assignments (human / service metadata; not authorization).</summary>
    public const string WorkTaskAssignments = "work_task_assignments";

    /// <summary>Per-Task completion policy with versioned evidence contract.</summary>
    public const string WorkTaskCompletionPolicies = "work_task_completion_policies";

    /// <summary>Mirror dedupe ledger (the Work-side inbox; one row per claimed message id).</summary>
    public const string InboxReceipts = "inbox_receipts";

    /// <summary>Per-type outbox-watermark for the Work-side subscribers (one row per type+key).</summary>
    public const string OutboxWatermarks = "outbox_watermarks";
}
