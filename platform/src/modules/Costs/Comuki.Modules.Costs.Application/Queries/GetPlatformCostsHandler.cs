using Comuki.Modules.Costs.Application.Ports;
using Comuki.Modules.Costs.Application.Views;

namespace Comuki.Modules.Costs.Application.Queries;

/// <summary>
/// Builds the platform-wide cost rollup (<c>GET /api/v1/costs</c>) from
/// <see cref="IPlatformCostAggregator"/> slices — the all-projects sibling
/// of <see cref="GetProjectCostsHandler"/>.
/// </summary>
/// <param name="aggregator"></param>
/// <param name="clock"></param>
public sealed class GetPlatformCostsHandler(IPlatformCostAggregator aggregator, TimeProvider clock)
{
    /// <summary>Default rollup window when the caller sends no <c>days</c>.</summary>
    public const int DefaultWindowDays = 30;

    /// <summary>Window bounds: [1, 365].</summary>
    public const int MaxWindowDays = 365;

    /// <summary>Returns the platform rollup over the (clamped) day window.</summary>
    /// <param name="windowDays">Requested window; clamped to [1, <see cref="MaxWindowDays"/>].</param>
    /// <param name="cancellationToken"></param>
    public async Task<PlatformCostsView> HandleAsync(int? windowDays, CancellationToken cancellationToken = default)
    {
        var days = Math.Clamp(windowDays ?? DefaultWindowDays, 1, MaxWindowDays);
        var since = clock.GetUtcNow().AddDays(-days);

        var windowTotal = await aggregator.SumAllCostUsdMicrosAsync(since, cancellationToken);
        var allTimeTotal = await aggregator.SumAllCostUsdMicrosAsync(cancellationToken: cancellationToken);
        var byProject = await aggregator.ListProjectSlicesAsync(since, cancellationToken);
        var byDay = await aggregator.ListDaySlicesAsync(since, cancellationToken);

        return new PlatformCostsView(
            since,
            days,
            windowTotal,
            allTimeTotal,
            [.. byProject.Select(static slice => new ProjectCostSliceView(slice.ProjectId.Value, slice.CostUsdMicros, slice.Runs))],
            [.. byDay.Select(static slice => new DayCostSliceView(slice.Date, slice.CostUsdMicros))]);
    }
}
