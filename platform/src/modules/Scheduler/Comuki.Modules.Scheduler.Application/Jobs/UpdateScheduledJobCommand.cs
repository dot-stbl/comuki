using Comuki.Modules.Scheduler.Domain.Ids;

namespace Comuki.Modules.Scheduler.Application.Jobs;

/// <summary>Partial update — null fields leave the stored value untouched.</summary>
/// <param name="JobId"></param>
/// <param name="CronExpression">Null keeps the cron; non-null is re-parsed.</param>
/// <param name="ProfileKey"></param>
/// <param name="Enabled"></param>
public sealed record UpdateScheduledJobCommand(
    ScheduledJobId JobId,
    string? CronExpression,
    string? ProfileKey,
    bool? Enabled);
