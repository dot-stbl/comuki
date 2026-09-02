using Comuki.Shared.Kernel.Ids;

namespace Comuki.Shared.Contracts.Costs;

/// <summary>
/// Host-composed port: when a hard budget is exceeded, cancel the run and
/// append a journal entry. Costs never references the orchestration engine
/// directly — the host wires this to <c>IRunJournal</c> + run cancel.
/// </summary>
public interface IBudgetGate
{
    /// <summary>
    /// Hard-stops the run (cancel + journal <c>budget.exceeded</c>). Idempotent
    /// when the run is already terminal.
    /// </summary>
    /// <param name="runId"></param>
    /// <param name="projectId"></param>
    /// <param name="spentUsdMicros"></param>
    /// <param name="hardLimitUsdMicros"></param>
    /// <param name="cancellationToken"></param>
    public Task HardStopAsync(
        RunId runId,
        ProjectId projectId,
        long spentUsdMicros,
        long hardLimitUsdMicros,
        CancellationToken cancellationToken = default);
}
