using Comuki.Modules.Scheduler.Domain.Jobs;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Scheduler.Application.Ports;

/// <summary>
/// The dispatcher's contract — the scheduler module's only way to fire a
/// run. The module never references the engine: the host composes the
/// implementation (mirroring the intake module's <c>IRunLauncher</c>
/// port), which writes the run + work item through the orchestration
/// context.
/// </summary>
public interface ISchedulerDispatcher
{
    /// <summary>
    /// Launches one run from the given scheduled job; returns the run id.
    /// The job carries the project, profile key, brief json and (when
    /// set) the one-shot instant — the dispatcher just materialises them
    /// into the engine's domain shape.
    /// </summary>
    /// <param name="job">The job that fired.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The created run id.</returns>
    public Task<RunId> DispatchAsync(ScheduledJob job, CancellationToken cancellationToken = default);
}
