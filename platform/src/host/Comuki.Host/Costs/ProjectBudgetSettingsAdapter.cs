using Comuki.Modules.Projects.Application.Ports;
using Comuki.Shared.Contracts.Costs;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Host.Costs;

/// <summary>
/// Reads soft/hard budget caps from the live Projects settings store
/// (cache-backed). Singleton — the store is singleton.
/// </summary>
/// <param name="settings"></param>
public sealed class ProjectBudgetSettingsAdapter(IProjectSettingsStore settings) : IProjectBudgetSettings
{
    /// <inheritdoc />
    public async Task<ProjectBudgetCaps> GetAsync(ProjectId projectId, CancellationToken cancellationToken = default)
    {
        var cached = settings.GetCached(projectId);
        var row = cached ?? await settings.FindAsync(projectId, cancellationToken);
        return row is null
            ? new ProjectBudgetCaps(null, null)
            : new ProjectBudgetCaps(row.SoftBudgetUsdMicros, row.HardBudgetUsdMicros);
    }
}
