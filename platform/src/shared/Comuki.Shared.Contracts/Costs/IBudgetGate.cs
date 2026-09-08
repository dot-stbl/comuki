using Comuki.Shared.Kernel.Exceptions;
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

    /// <summary>
    /// Claim-time budget gate: reads the project's soft/hard caps and
    /// current spend, then decides whether the claim may proceed. Throws
    /// <see cref="BudgetExceededException"/> with code
    /// <c>budget.hard_exceeded</c> when the project's hard cap is met or
    /// exceeded (maps to HTTP 402). When only the soft cap is met, logs a
    /// warning and returns normally — the soft cap is advisory, not a deny
    /// signal. No cap configured, or spend below the soft cap, is a no-op.
    /// </summary>
    /// <param name="projectId">Project the new claim attributes to.</param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="BudgetExceededException">Hard cap met or exceeded; the claim is denied.</exception>
    public Task EnforceClaimAsync(
        ProjectId projectId,
        CancellationToken cancellationToken = default);
}
