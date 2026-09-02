using Comuki.Shared.Contracts.Costs;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Costs.Application.Budgets;

/// <summary>
/// Default budget settings: no soft/hard caps. Host replaces with the
/// Projects settings adapter.
/// </summary>
public sealed class UnlimitedBudgetSettings : IProjectBudgetSettings
{
    /// <inheritdoc />
    public Task<ProjectBudgetCaps> GetAsync(ProjectId projectId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ProjectBudgetCaps(null, null));
    }
}
