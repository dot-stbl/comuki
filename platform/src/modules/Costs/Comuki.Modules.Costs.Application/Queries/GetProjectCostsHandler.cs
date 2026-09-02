using Comuki.Modules.Costs.Application.Ports;
using Comuki.Modules.Costs.Application.Views;
using Comuki.Shared.Contracts.Costs;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Costs.Application.Queries;

/// <summary>Builds the project costs read model for the API.</summary>
/// <param name="store"></param>
/// <param name="budgets"></param>
public sealed class GetProjectCostsHandler(IUsageEventStore store, IProjectBudgetSettings budgets)
{
    /// <summary>Default number of recent events in the feed.</summary>
    public const int DefaultRecentTake = 50;

    /// <summary>Returns the cost summary for <paramref name="projectId"/>.</summary>
    /// <param name="projectId"></param>
    /// <param name="cancellationToken"></param>
    public async Task<ProjectCostsView> HandleAsync(ProjectId projectId, CancellationToken cancellationToken = default)
    {
        var caps = await budgets.GetAsync(projectId, cancellationToken);
        var spent = await store.SumProjectCostUsdMicrosAsync(projectId, cancellationToken: cancellationToken);
        var recent = await store.ListRecentAsync(projectId, DefaultRecentTake, cancellationToken);

        return new ProjectCostsView(
            projectId,
            spent,
            caps.SoftLimitUsdMicros,
            caps.HardLimitUsdMicros,
            SoftExceeded: caps.SoftLimitUsdMicros is { } soft && spent >= soft,
            HardExceeded: caps.HardLimitUsdMicros is { } hard && spent >= hard,
            [.. recent.Select(UsageEventMapper.ToView)]);
    }
}
