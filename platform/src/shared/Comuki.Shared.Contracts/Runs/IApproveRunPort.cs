using Comuki.Shared.Kernel.Ids;

namespace Comuki.Shared.Contracts.Runs;

/// <summary>
/// Decision port: a human (dashboard, chat interrupt) approves a run that
/// is currently escalated back to a gate. Implemented in the host composition
/// root over the orchestration context — modules never reference the engine.
/// </summary>
public interface IApproveRunPort
{
    /// <summary>
    /// Approves the run. The transition is legal from <c>Escalated</c> only;
    /// any other source status, or a missing run, raises a typed exception
    /// mapped by the central <c>ProviderExceptionHandler</c>. The port also
    /// appends a <c>run.status_changed</c> journal entry in the same
    /// transaction.
    /// </summary>
    /// <param name="runId">Run to approve.</param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="Kernel.Exceptions.ProviderNotFoundException">
    /// The run does not exist or is out of scope.
    /// </exception>
    /// <exception cref="Kernel.Exceptions.DomainException">
    /// The run is not in <c>Escalated</c> status; no other target is legal.
    /// </exception>
    public Task ApproveAsync(RunId runId, CancellationToken cancellationToken = default);
}
