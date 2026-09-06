namespace Comuki.Host.Scheduler.Models;

/// <summary>
/// Scheduled job creation body
/// (<c>POST /api/v1/projects/{projectId}/scheduled-jobs</c>). The
/// dispatcher stores the brief jsonb verbatim and the
/// <c>profileKey</c> drives the work item the launched run resolves.
/// <c>enabled</c> defaults to <c>true</c> when omitted; a one-shot
/// <c>runOnOnceAt</c> in the past fast-forwards the dispatcher's first
/// poll (useful for smoke tests).
/// </summary>
public sealed class CreateScheduledJobRequest
{
    /// <summary>5-field UTC cron expression.</summary>
    public required string CronExpression { get; init; } = string.Empty;

    /// <summary>Profile the launched run will resolve.</summary>
    public required string ProfileKey { get; init; } = string.Empty;

    /// <summary>Worker brief payload (jsonb).</summary>
    public required string BriefJson { get; init; } = "{}";

    /// <summary>Optional one-shot fire-at; null means cron-only.</summary>
    public DateTimeOffset? RunOnOnceAt { get; init; }

    /// <summary>Disable the job at create time (defaults to enabled).</summary>
    public bool? Enabled { get; init; }
}
