namespace Comuki.Host.Scheduler.Models;

/// <summary>
/// Scheduled job partial-update body
/// (<c>PATCH /api/v1/projects/{projectId}/scheduled-jobs/{jobId}</c>).
/// Null fields leave the stored value untouched — the canonical PATCH
/// semantics. <c>cronExpression</c> is re-parsed on write; a malformed
/// value surfaces <c>400</c> with the <c>scheduler.invalid_cron</c>
/// code.
/// </summary>
public sealed class UpdateScheduledJobRequest
{
    /// <summary>Null keeps the cron; non-null is re-parsed.</summary>
    public string? CronExpression { get; init; }

    /// <summary>Worker brief payload replacement; null keeps the stored value.</summary>
    public string? BriefJson { get; init; }

    /// <summary>Null keeps the flag; false disables the dispatcher for this job.</summary>
    public bool? Enabled { get; init; }
}
