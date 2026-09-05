using Comuki.Modules.Scheduler.Domain.Jobs;

namespace Comuki.Modules.Scheduler.Application.Views;

/// <summary>
/// Read projection of <see cref="ScheduledJob"/> for the REST surface.
/// </summary>
/// <param name="Id">The job id.</param>
/// <param name="ProjectId">Owning project.</param>
/// <param name="CronExpression">5-field UTC cron expression.</param>
/// <param name="ProfileKey">Profile the launched run will resolve.</param>
/// <param name="BriefJson">Worker brief payload.</param>
/// <param name="RunOnOnceAt">Optional one-shot fire-at.</param>
/// <param name="Enabled">Whether the dispatcher will fire it.</param>
/// <param name="LastFiredAt">Last fire stamp; null until the first dispatch.</param>
/// <param name="NextFireAt">Next computed fire stamp.</param>
/// <param name="CreatedAt"></param>
/// <param name="UpdatedAt"></param>
public sealed record ScheduledJobView(
    Guid Id,
    Guid ProjectId,
    string CronExpression,
    string ProfileKey,
    string BriefJson,
    DateTimeOffset? RunOnOnceAt,
    bool Enabled,
    DateTimeOffset? LastFiredAt,
    DateTimeOffset NextFireAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>Maps a domain aggregate to its read projection.</summary>
    /// <param name="job"></param>
    public static ScheduledJobView Of(ScheduledJob job)
    {
        return new ScheduledJobView(
            Id: job.Id.Value,
            ProjectId: job.ProjectId.Value,
            CronExpression: job.CronExpression,
            ProfileKey: job.ProfileKey,
            BriefJson: job.BriefJson,
            RunOnOnceAt: job.RunOnOnceAt,
            Enabled: job.Enabled,
            LastFiredAt: job.LastFiredAt,
            NextFireAt: job.NextFireAt,
            CreatedAt: job.CreatedAt,
            UpdatedAt: job.UpdatedAt);
    }

    /// <summary>Maps a list of domain aggregates to their read projections.</summary>
    /// <param name="jobs"></param>
    public static IReadOnlyList<ScheduledJobView> OfAll(IEnumerable<ScheduledJob> jobs)
    {
        return [.. jobs.Select(Of)];
    }
}
