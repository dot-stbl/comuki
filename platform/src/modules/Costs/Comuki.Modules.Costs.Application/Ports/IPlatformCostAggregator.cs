using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Costs.Application.Ports;

/// <summary>
/// Platform-wide usage-event aggregation for the costs rollup
/// (<c>GET /api/v1/costs</c>) — the all-projects sibling of the per-project
/// sums on <see cref="Shared.Contracts.Usage.IUsageEventStore"/>.
/// Behind a port so the handler stays unit-testable against a fake.
/// </summary>
public interface IPlatformCostAggregator
{
    /// <summary>Sums cost over every project since <paramref name="since"/> (UTC), null = all time.</summary>
    /// <param name="since"></param>
    /// <param name="cancellationToken"></param>
    public Task<long> SumAllCostUsdMicrosAsync(DateTimeOffset? since = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Per-project spend since <paramref name="since"/> plus the distinct
    /// run count each project's spend is attributed to — greatest spend first.
    /// </summary>
    /// <param name="since"></param>
    /// <param name="cancellationToken"></param>
    public Task<IReadOnlyList<ProjectCostSlice>> ListProjectSlicesAsync(
        DateTimeOffset? since = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Per-UTC-day spend since <paramref name="since"/>, oldest day first.
    /// Days with no events are absent (sparse series), not zero-filled —
    /// the caller knows the window and can pad.
    /// </summary>
    /// <param name="since"></param>
    /// <param name="cancellationToken"></param>
    public Task<IReadOnlyList<DayCostSlice>> ListDaySlicesAsync(
        DateTimeOffset? since = null,
        CancellationToken cancellationToken = default);
}

/// <summary>One project's spend slice of the rollup window.</summary>
/// <param name="ProjectId">Project the spend is attributed to.</param>
/// <param name="CostUsdMicros">Window spend in USD micros.</param>
/// <param name="Runs">Distinct runs the window's events name (project-level events with no run are not counted).</param>
public sealed record ProjectCostSlice(
    ProjectId ProjectId,
    long CostUsdMicros,
    int Runs);

/// <summary>One UTC day of the spend series.</summary>
/// <param name="Date">The UTC day the events occurred on.</param>
/// <param name="CostUsdMicros">Day spend in USD micros.</param>
public sealed record DayCostSlice(
    DateOnly Date,
    long CostUsdMicros);
