using Comuki.Modules.Costs.Application.Ports;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Modules.Costs.Infrastructure.Persistence.Stores;

/// <summary>
/// EF implementation of <see cref="IPlatformCostAggregator"/> — grouped
/// sums over the <c>usage_events</c> table. Same singleton-over-factory
/// shape as <see cref="EfUsageEventStore"/>.
/// </summary>
/// <param name="factory"></param>
public sealed class EfPlatformCostAggregator(IDbContextFactory<CostsDbContext> factory) : IPlatformCostAggregator
{
    /// <inheritdoc />
    public async Task<long> SumAllCostUsdMicrosAsync(DateTimeOffset? since = null, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var query = db.UsageEvents.AsNoTracking();
        if (since is { } lower)
        {
            query = query.Where(usageEvent => usageEvent.OccurredAt >= lower);
        }

        return await query.SumAsync(static usageEvent => usageEvent.CostUsdMicros, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProjectCostSlice>> ListProjectSlicesAsync(
        DateTimeOffset? since = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var query = db.UsageEvents.AsNoTracking();
        if (since is { } lower)
        {
            query = query.Where(usageEvent => usageEvent.OccurredAt >= lower);
        }

        var slices = await query
            .GroupBy(usageEvent => usageEvent.ProjectId)
            .Select(static group => new
            {
                ProjectId = group.Key,
                CostUsdMicros = group.Sum(usageEvent => usageEvent.CostUsdMicros),
                Runs = group.Where(usageEvent => usageEvent.RunId != null)
                    .Select(usageEvent => usageEvent.RunId!.Value)
                    .Distinct()
                    .Count(),
            })
            .OrderByDescending(static slice => slice.CostUsdMicros)
            .ToListAsync(cancellationToken);

        return [.. slices.Select(static slice => new ProjectCostSlice(slice.ProjectId, slice.CostUsdMicros, slice.Runs))];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DayCostSlice>> ListDaySlicesAsync(
        DateTimeOffset? since = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var query = db.UsageEvents.AsNoTracking();
        if (since is { } lower)
        {
            query = query.Where(usageEvent => usageEvent.OccurredAt >= lower);
        }

        // DateOnly.FromDateTime is translator-supported on Npgsql; the
        // grouping keys on the UTC day the event occurred on.
        var slices = await query
            .GroupBy(usageEvent => DateOnly.FromDateTime(usageEvent.OccurredAt.UtcDateTime))
            .Select(static group => new
            {
                Date = group.Key,
                CostUsdMicros = group.Sum(usageEvent => usageEvent.CostUsdMicros),
            })
            .OrderBy(static slice => slice.Date)
            .ToListAsync(cancellationToken);

        return [.. slices.Select(static slice => new DayCostSlice(slice.Date, slice.CostUsdMicros))];
    }
}
