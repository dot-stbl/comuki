using Comuki.Shared.Contracts.Costs;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Costs.Application.Budgets;

/// <summary>
/// No-op <see cref="IProjectBudgetSettings"/> for hosts that have not yet
/// wired a real settings adapter. Safe default: both caps are <c>null</c>
/// (effectively unlimited), so the budget gate short-circuits to no hard-stop.
/// </summary>
public sealed class NullProjectBudgetSettings : IProjectBudgetSettings
{
    /// <inheritdoc />
    public Task<ProjectBudgetCaps> GetAsync(
        ProjectId projectId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ProjectBudgetCaps(null, null));
    }
}
