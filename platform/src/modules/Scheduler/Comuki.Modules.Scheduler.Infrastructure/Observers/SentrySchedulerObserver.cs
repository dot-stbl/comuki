using Comuki.Modules.Scheduler.Application.Observers;
using Comuki.Modules.Scheduler.Application.Options;
using Comuki.Modules.Scheduler.Domain.Ids;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comuki.Modules.Scheduler.Infrastructure.Observers;

/// <summary>
/// Sentry side-channel for scheduler fires. When
/// <see cref="SchedulerSentryOptions.Dsn"/> is null or whitespace the
/// observer is a no-op (the SDK was never initialised by the host, and
/// even if a stray <c>CaptureEvent</c> were made it would be dropped by
/// the SDK — but we gate early anyway to keep the call site clean).
/// <para>
/// Each fire becomes a <c>SentryLevel.Info</c> event tagged with
/// <c>scheduler.job_id</c>, <c>scheduler.project_id</c> and
/// <c>scheduler.profile_key</c>. The event is informational by design:
/// "the scheduler dispatched a run" is a normal platform event, not an
/// error. Operators wire Sentry alert rules on tag transitions if they
/// want noise; the journal observer carries the durable timeline for
/// audit / replay.
/// </para>
/// </summary>
/// <param name="options">Bound scheduler options — DSN gate.</param>
/// <param name="logger">Structured logger for SDK diagnostics.</param>
public sealed class SentrySchedulerObserver(
    IOptions<SchedulerOptions> options,
    ILogger<SentrySchedulerObserver> logger) : ISchedulerObserver
{
    /// <inheritdoc />
    public Task OnJobFiredAsync(
        ScheduledJobId jobId,
        ProjectId projectId,
        string profileKey,
        RunId runId,
        DateTimeOffset firedAt,
        CancellationToken cancellationToken = default)
    {
        var sentryOptions = options.Value.Sentry;
        if (string.IsNullOrWhiteSpace(sentryOptions.Dsn))
        {
            return Task.CompletedTask;
        }

        try
        {
            var evt = new SentryEvent
            {
                Message = "scheduler.job_fired",
                Level = SentryLevel.Info,
            };
            evt.SetTag(SchedulerSentryTags.JobId, jobId.Value.ToString());
            evt.SetTag(SchedulerSentryTags.ProjectId, projectId.Value.ToString());
            evt.SetTag(SchedulerSentryTags.ProfileKey, profileKey);
            evt.SetExtra(SchedulerSentryTags.RunId, runId.Value.ToString());
            evt.SetExtra(SchedulerSentryTags.FiredAt, firedAt);

            // capture returns the event id (or null when the SDK is
            // disabled) — we discard intentionally; fire is the path
            // of record, not the Sentry capture.
            _ = SentrySdk.CaptureEvent(evt);
        }
        catch (Exception exception)
        {
            // boundary: an Sentry transport failure (DNS, 4xx, etc.)
            // must not abort the fire path — the dispatcher has already
            // stamped the run + the journal observer has already
            // appended its row. Log and move on; the operator can wire
            // a separate Sentry health probe if they care.
            logger.LogWarning(
                exception,
                "Sentry capture failed for scheduler job {JobId}; fire is unaffected",
                jobId.Value);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Tag keys the Sentry observer stamps on every fire event. Bounded
/// cardinality on the values: <c>scheduler.job_id</c> and
/// <c>scheduler.project_id</c> are uuid strings — Sentry groups by tag
/// value, so this is per-id uniqueness and the dashboard becomes hard to
/// slice; operators use Sentry's search by full id, not aggregation.
/// That is intentional — the journal row is the aggregation key, Sentry
/// is the per-fire audit channel.
/// </summary>
file static class SchedulerSentryTags
{
    public const string JobId = "scheduler.job_id";
    public const string ProjectId = "scheduler.project_id";
    public const string ProfileKey = "scheduler.profile_key";
    public const string RunId = "scheduler.run_id";
    public const string FiredAt = "scheduler.fired_at";
}
