using Comuki.Modules.Costs.Application.Ports;
using Comuki.Modules.Costs.Domain.Events;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Modules.Costs.Infrastructure.Persistence.Stores;

/// <summary>
/// EF implementation of <see cref="IUsageEventStore"/>. Singleton over the
/// context factory — every method opens its own context (same shape as
/// Memory's store).
/// </summary>
/// <param name="factory"></param>
public sealed class EfUsageEventStore(IDbContextFactory<CostsDbContext> factory) : IUsageEventStore
{
    /// <inheritdoc />
    public async Task AddAsync(UsageEvent usageEvent, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        _ = db.UsageEvents.Add(usageEvent);
        _ = await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<long> SumProjectCostUsdMicrosAsync(
        ProjectId projectId,
        DateTimeOffset? since = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var query = db.UsageEvents.AsNoTracking().Where(usageEvent => usageEvent.ProjectId == projectId);
        if (since is { } lower)
        {
            query = query.Where(usageEvent => usageEvent.OccurredAt >= lower);
        }

        return await query.SumAsync(static usageEvent => usageEvent.CostUsdMicros, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<long> SumRunCostUsdMicrosAsync(RunId runId, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.UsageEvents.AsNoTracking()
            .Where(usageEvent => usageEvent.RunId == runId)
            .SumAsync(static usageEvent => usageEvent.CostUsdMicros, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UsageEvent>> ListRecentAsync(
        ProjectId projectId,
        int take,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.UsageEvents.AsNoTracking()
            .Where(usageEvent => usageEvent.ProjectId == projectId)
            .OrderByDescending(static usageEvent => usageEvent.OccurredAt)
            .ThenByDescending(static usageEvent => usageEvent.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
    }
}
