using Comuki.Modules.Scheduler.Domain.Ids;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Scheduler.Domain.Jobs;

/// <summary>
/// A single cron-scheduled run source for a project. When the background
/// service observes a job whose <see cref="NextFireAt"/> has passed, it
/// dispatches a run through the scheduler dispatcher and updates
/// <see cref="LastFiredAt"/> + the next-fire stamp in one transaction.
/// <para>
/// <see cref="Enabled"/> + <see cref="CronExpression"/> are the two
/// writable knobs (PATCH path); everything else is the immutable audit
/// trail. <see cref="BriefJson"/> is the goal payload the launched worker
/// reads — same shape as the intake brief, so a downstream worker cannot
/// tell whether it was launched from a webhook or from the scheduler.
/// </para>
/// </summary>
public sealed class ScheduledJob
{
    internal ScheduledJob()
    {
    }

    /// <summary>Strong-typed job id.</summary>
    public ScheduledJobId Id { get; private set; }

    /// <summary>Owning project (the run, when launched, is project-scoped).</summary>
    public ProjectId ProjectId { get; private set; }

    /// <summary>5-field UTC cron expression.</summary>
    public string CronExpression { get; private set; } = string.Empty;

    /// <summary>Profile key the launched run will resolve through the control plane.</summary>
    public string ProfileKey { get; private set; } = string.Empty;

    /// <summary>Worker brief payload (jsonb) — same shape as the intake / chat briefs.</summary>
    public string BriefJson { get; private set; } = string.Empty;

    /// <summary>
    /// Optional one-shot fire-at: when set, the dispatcher fires once at
    /// this instant and then disables the job. Null means the cron is the
    /// only schedule. The smoke test uses this to fast-forward time.
    /// </summary>
    public DateTimeOffset? RunOnOnceAt { get; private set; }

    /// <summary>Disabled jobs are skipped by the dispatcher but stay in the table.</summary>
    public bool Enabled { get; private set; }

    /// <summary>Last fire instant — null until the dispatcher has run once.</summary>
    public DateTimeOffset? LastFiredAt { get; private set; }

    /// <summary>
    /// Next computed fire instant — the dispatcher updates this in the
    /// same transaction it stamps <see cref="LastFiredAt"/>. The poll
    /// query filters on <c>next_fire_at &lt;= now</c>.
    /// </summary>
    public DateTimeOffset NextFireAt { get; private set; }

    /// <summary>Created timestamp.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Last mutation timestamp (PATCHes touch this).</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Creates a new job: parses the cron expression eagerly (malformed
    /// input is a programmer / caller error), and seeds <c>NextFireAt</c>
    /// from <paramref name="now"/> so the dispatcher never has to compute
    /// the first fire on the poll path.
    /// </summary>
    /// <param name="projectId"></param>
    /// <param name="cronExpression">5-field UTC cron expression.</param>
    /// <param name="profileKey"></param>
    /// <param name="briefJson"></param>
    /// <param name="runOnOnceAt">Optional one-shot instant.</param>
    /// <param name="now"></param>
    /// <exception cref="FormatException">Cron expression did not parse.</exception>
    public static ScheduledJob Create(
        ProjectId projectId,
        string cronExpression,
        string profileKey,
        string briefJson,
        DateTimeOffset? runOnOnceAt,
        DateTimeOffset now)
    {
        var cron = Scheduling.CronExpression.Parse(cronExpression);
        var initialNext = runOnOnceAt ?? cron.NextFireAfter(now) ?? now.AddMinutes(1);

        return new ScheduledJob
        {
            Id = ScheduledJobId.New(),
            ProjectId = projectId,
            CronExpression = cronExpression.Trim(),
            ProfileKey = profileKey.Trim(),
            BriefJson = briefJson,
            RunOnOnceAt = runOnOnceAt,
            Enabled = true,
            LastFiredAt = null,
            NextFireAt = initialNext,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>Partial update: null fields leave the stored value untouched.</summary>
    /// <param name="cronExpression">Null keeps the cron; non-null is re-parsed.</param>
    /// <param name="profileKey"></param>
    /// <param name="enabled"></param>
    /// <param name="now"></param>
    /// <exception cref="FormatException">Cron expression did not parse.</exception>
    public void Update(string? cronExpression, string? profileKey, bool? enabled, DateTimeOffset now)
    {
        if (cronExpression is { Length: > 0 } nextCron)
        {
            var parsed = Scheduling.CronExpression.Parse(nextCron);
            CronExpression = nextCron.Trim();
            NextFireAt = parsed.NextFireAfter(now) ?? now.AddMinutes(1);
        }

        if (profileKey is { Length: > 0 } nextProfile)
        {
            ProfileKey = nextProfile.Trim();
        }

        if (enabled is { } nextEnabled)
        {
            Enabled = nextEnabled;
        }

        UpdatedAt = now;
    }

    /// <summary>
    /// Stamps the fire trail: <paramref name="lastFiredAt"/> on
    /// <see cref="LastFiredAt"/>, computes the next fire through the cron
    /// from the same anchor (so a one-shot <see cref="RunOnOnceAt"/> job
    /// advances to the next cron tick immediately after firing).
    /// </summary>
    /// <param name="lastFiredAt"></param>
    /// <exception cref="FormatException">The stored cron expression is malformed (data drift).</exception>
    public void MarkFired(DateTimeOffset lastFiredAt)
    {
        var cron = Scheduling.CronExpression.Parse(CronExpression);
        var nextFromCron = cron.NextFireAfter(lastFiredAt) ?? lastFiredAt.AddMinutes(1);
        LastFiredAt = lastFiredAt;
        NextFireAt = nextFromCron;

        // a one-shot job becomes a regular cron job after the first fire —
        // RunOnOnceAt stays as the audit trail, but the dispatcher keys
        // off NextFireAt which is now derived from the cron.
        UpdatedAt = lastFiredAt;
    }
}
