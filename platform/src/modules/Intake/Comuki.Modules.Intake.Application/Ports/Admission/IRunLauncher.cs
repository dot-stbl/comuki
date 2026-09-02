using Comuki.Modules.Intake.Domain.Tickets;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Intake.Application.Ports.Admission;

/// <summary>
/// The run-launch port — the module's only way to create a run. The
/// module never references the engine: the host composes the
/// implementation (mirroring <c>ChatRunStarter</c>), which writes the
/// run and its first work item through the orchestration context.
/// </summary>
public interface IRunLauncher
{
    /// <summary>Launches the run for an admitted ticket; returns the created run id.</summary>
    /// <param name="projectId"></param>
    /// <param name="ticket"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<RunId> LaunchAsync(ProjectId projectId, IncomingTicket ticket, CancellationToken cancellationToken = default);
}
