using Comuki.Modules.Scheduler.Domain.Ids;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Scheduler.Application.Observers;

/// <summary>
/// Sink for a successful scheduler fire. The dispatcher calls every
/// registered observer after a run has been launched and the fire trail
/// stamped on the job, so observers are <em>side-channels</em> (journal,
/// Sentry, future chat-notify) — they never gate the fire path itself.
/// Observers must be cheap and tolerant of partial failures: one
/// observer throwing is caught and logged by the dispatcher; the rest
/// of the batch continues.
/// </summary>
public interface ISchedulerObserver
{
    /// <summary>
    /// Notifies the observer that the dispatcher has fired
    /// <paramref name="jobId"/>. Implementations publish the event to
    /// their respective transport (journal row, Sentry capture, etc.).
    /// </summary>
    /// <param name="jobId">Strong-typed id of the fired scheduled job.</param>
    /// <param name="projectId">Project the fired job belongs to.</param>
    /// <param name="profileKey">Catalog profile the launched run claims on.</param>
    /// <param name="runId">Id of the orchestration run the dispatcher created.</param>
    /// <param name="firedAt">Wall-clock instant the dispatcher stamped on the fire.</param>
    /// <param name="cancellationToken">Propagated from the dispatcher loop.</param>
    public Task OnJobFiredAsync(
        ScheduledJobId jobId,
        ProjectId projectId,
        string profileKey,
        RunId runId,
        DateTimeOffset firedAt,
        CancellationToken cancellationToken = default);
}
