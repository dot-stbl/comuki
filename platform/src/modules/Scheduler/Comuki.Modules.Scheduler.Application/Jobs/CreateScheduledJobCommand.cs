using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Scheduler.Application.Jobs;

/// <summary>Create command for a scheduled job.</summary>
/// <param name="ProjectId"></param>
/// <param name="CronExpression">5-field UTC cron expression.</param>
/// <param name="ProfileKey">Profile the launched run will resolve.</param>
/// <param name="BriefJson">Worker brief payload (jsonb).</param>
/// <param name="RunOnOnceAt">Optional one-shot fire-at; null means cron-only.</param>
public sealed record CreateScheduledJobCommand(
    ProjectId ProjectId,
    string CronExpression,
    string ProfileKey,
    string BriefJson,
    DateTimeOffset? RunOnOnceAt);
