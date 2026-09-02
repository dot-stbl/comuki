using Comuki.Shared.Contracts.Costs;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Costs.Application.Budgets;

/// <summary>
/// No-op budget gate used until the host wires a real cancel+journal
/// adapter. Safe default: recording still works, hard-stop is a no-op.
/// </summary>
public sealed class NullBudgetGate : IBudgetGate
{
    /// <inheritdoc />
    public Task HardStopAsync(
        RunId runId,
        ProjectId projectId,
        long spentUsdMicros,
        long hardLimitUsdMicros,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
