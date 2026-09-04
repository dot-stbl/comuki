using Comuki.Shared.Kernel.Ids;

namespace Comuki.Shared.Contracts.Runs;

/// <summary>
/// Decision port: a human cancels a run that's still in flight. The engine
/// already cancels a run from many internal flows (budget gate, work-item
/// reaper); this port is the operator-facing surface that completes the
/// set. Implemented in the host composition root over the orchestration
/// context — modules never reference the engine.
/// </summary>
public interface ICancelRunPort
{
    /// <summary>
    /// Cancels the run. The transition is legal from every non-terminal
    /// status (<c>Queued</c>, <c>Waiting</c>, <c>Running</c>,
    /// <c>Escalated</c>); terminal runs (<c>Succeeded</c>, <c>Cancelled</c>)
    /// raise a <see>DomainException</see> with the
    /// <c>run.terminal_state</c> code. The port appends a
    /// <c>run.status_changed</c> journal entry in the same transaction;
    /// when <paramref name="reason"/> is non-empty, the entry's payload
    /// carries it as a <c>reason</c> field.
    /// </summary>
    /// <param name="runId">Run to cancel.</param>
    /// <param name="reason">Optional human-readable reason — surfaced in the journal payload.</param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="Kernel.Exceptions.ProviderNotFoundException">
    /// The run does not exist or is out of scope.
    /// </exception>
    /// <exception cref="Kernel.Exceptions.DomainException">
    /// The run is in a terminal status.
    /// </exception>
    public Task CancelAsync(RunId runId, string? reason, CancellationToken cancellationToken = default);
}
