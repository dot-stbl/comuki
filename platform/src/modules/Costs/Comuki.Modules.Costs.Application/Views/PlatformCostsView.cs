namespace Comuki.Modules.Costs.Application.Views;

/// <summary>
/// Platform-wide cost rollup for <c>GET /api/v1/costs</c>. Money fields are
/// USD micros (1 USD = 1_000_000) — same unit as the per-project view.
/// </summary>
/// <param name="Since">Inclusive window start (UTC instant).</param>
/// <param name="WindowDays">Window length in days.</param>
/// <param name="WindowUsdMicros">Window spend across every project.</param>
/// <param name="AllTimeUsdMicros">All-time spend across every project.</param>
/// <param name="ByProject">Per-project window slices, greatest spend first.</param>
/// <param name="ByDay">Per-UTC-day window spend, oldest day first (sparse).</param>
public sealed record PlatformCostsView(
    DateTimeOffset Since,
    int WindowDays,
    long WindowUsdMicros,
    long AllTimeUsdMicros,
    IReadOnlyList<ProjectCostSliceView> ByProject,
    IReadOnlyList<DayCostSliceView> ByDay);

/// <summary>Wire row of one project's slice.</summary>
/// <param name="ProjectId">Project the spend is attributed to.</param>
/// <param name="CostUsdMicros">Window spend in USD micros.</param>
/// <param name="Runs">Distinct runs the window's events name.</param>
public sealed record ProjectCostSliceView(
    Guid ProjectId,
    long CostUsdMicros,
    int Runs);

/// <summary>Wire row of one UTC day of the series.</summary>
/// <param name="Date">The UTC day (ISO date).</param>
/// <param name="CostUsdMicros">Day spend in USD micros.</param>
public sealed record DayCostSliceView(
    DateOnly Date,
    long CostUsdMicros);
