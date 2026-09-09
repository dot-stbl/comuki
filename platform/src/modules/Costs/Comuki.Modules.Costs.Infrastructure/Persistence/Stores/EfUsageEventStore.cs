using Comuki.Modules.Costs.Domain.Events;
using Comuki.Shared.Contracts.Usage;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Modules.Costs.Infrastructure.Persistence.Stores;

/// <summary>
/// EF implementation of <see cref="IUsageEventStore"/>. Singleton over the
/// context factory — every method opens its own context (same shape as
/// Memory's store). The interface moved to
/// <c>Comuki.Shared.Contracts.Usage</c> so cross-module callers (Proxy)
/// don't need a reference to <c>Costs.Application</c>; the store builds
/// the internal <see cref="UsageEvent"/> entity from the wire
/// <see cref="UsageRecord"/> itself, and projects to
/// <see cref="UsageEventSummary"/> for read paths so the boundary never
/// leaks the Costs.Domain type.
/// </summary>
/// <param name="factory"></param>
public sealed class EfUsageEventStore(IDbContextFactory<CostsDbContext> factory) : IUsageEventStore
{
    /// <inheritdoc />
    public async Task AddAsync(UsageRecord record, CancellationToken cancellationToken = default)
    {
        var source = UsageSourceKeys.Parse(record.Source);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        db.UsageEvents.Add(UsageEvent.Create(
            record.ProjectId,
            record.RunId,
            source,
            record.Model,
            record.InputTokens,
            record.OutputTokens,
            record.CostUsdMicros,
            record.OccurredAt));
        await db.SaveChangesAsync(cancellationToken);
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
    public async Task<long> SumProjectCostBySourceAsync(
        ProjectId projectId,
        string source,
        DateTimeOffset? since = null,
        CancellationToken cancellationToken = default)
    {
        var sourceEnum = UsageSourceKeys.Parse(source);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var query = db.UsageEvents.AsNoTracking()
            .Where(usageEvent => usageEvent.ProjectId == projectId && usageEvent.Source == sourceEnum);
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
    public async Task<IReadOnlyList<UsageEventSummary>> ListRecentAsync(
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
            .Select(static usageEvent => new UsageEventSummary(
                usageEvent.Id.Value,
                usageEvent.RunId,
                UsageSourceKeys.Of(usageEvent.Source),
                usageEvent.Model,
                usageEvent.InputTokens,
                usageEvent.OutputTokens,
                usageEvent.CostUsdMicros,
                usageEvent.OccurredAt))
            .ToListAsync(cancellationToken);
    }
}
