using Comuki.Modules.Scheduler.Application.Ports;
using Comuki.Modules.Scheduler.Domain.Ids;
using Comuki.Modules.Scheduler.Domain.Jobs;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Modules.Scheduler.Infrastructure.Persistence.Stores;

/// <summary>
/// <see cref="IScheduledJobStore"/> over the <see cref="SchedulerDbContext"/>.
/// The due-job query runs with <c>FOR UPDATE SKIP LOCKED</c> so two host
/// replicas can never claim the same row.
/// </summary>
/// <param name="db">Scheduler context of the current scope.</param>
public sealed class ScheduledJobStore(SchedulerDbContext db) : IScheduledJobStore
{
    /// <inheritdoc />
    public async Task AddAsync(ScheduledJob job, CancellationToken cancellationToken = default)
    {
        db.ScheduledJobs.Add(job);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<ScheduledJob?> FindAsync(ScheduledJobId jobId, CancellationToken cancellationToken = default)
    {
        return db.ScheduledJobs
            .FirstOrDefaultAsync(job => job.Id == jobId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ScheduledJob>> ListAsync(ProjectId projectId, CancellationToken cancellationToken = default)
    {
        return await db.ScheduledJobs
            .AsNoTracking()
            .Where(job => job.ProjectId == projectId)
            .OrderByDescending(job => job.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ScheduledJobPage> ListPagedAsync(
        ProjectId projectId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        // Clamp at the boundary so the controller does not have to know
        // the storage layer's limits. Same shape the controller used to
        // apply in-memory: pageSize [1, 500], page >= 1.
        var clampedPage = page < 1 ? 1 : page;
        var clampedSize = Math.Clamp(pageSize, 1, 500);
        var skip = (clampedPage - 1) * clampedSize;

        // The query is composed once and reused for both the count and the
        // page — EF Core 8+ translates Skip/Take into `OFFSET $skip LIMIT $take`
        // so Postgres returns at most `clampedSize` rows regardless of how
        // big the project is. OrderByDescending on a stable column makes
        // page-to-page order deterministic across requests.
        var scoped = db.ScheduledJobs
            .AsNoTracking()
            .Where(job => job.ProjectId == projectId);

        var total = await scoped.CountAsync(cancellationToken);

        var items = await scoped
            .OrderByDescending(job => job.CreatedAt)
            .Skip(skip)
            .Take(clampedSize)
            .ToListAsync(cancellationToken);

        return new ScheduledJobPage(items, total);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(ScheduledJob job, CancellationToken cancellationToken = default)
    {
        db.ScheduledJobs.Update(job);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(ScheduledJobId jobId, CancellationToken cancellationToken = default)
    {
        var existing = await db.ScheduledJobs
            .FirstOrDefaultAsync(job => job.Id == jobId, cancellationToken);
        if (existing is null)
        {
            return;
        }

        db.ScheduledJobs.Remove(existing);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ScheduledJob>> ListDueAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken = default)
    {
        return await db.ScheduledJobs
            .FromSqlRaw(
                """
                SELECT * FROM scheduler.scheduled_jobs
                WHERE enabled = TRUE AND next_fire_at <= {0}
                ORDER BY next_fire_at
                LIMIT {1}
                FOR UPDATE SKIP LOCKED
                """,
                now.UtcDateTime,
                limit)
            .ToListAsync(cancellationToken);
    }
}
