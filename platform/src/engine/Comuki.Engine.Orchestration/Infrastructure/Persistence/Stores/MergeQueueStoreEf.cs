using Comuki.Engine.Orchestration.Domain.MergeQueue;
using Comuki.Engine.Orchestration.Infrastructure.Persistence.Ports;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Engine.Orchestration.Infrastructure.Persistence.Stores;

/// <summary>
/// Postgres implementation of <see cref="IMergeQueueStore"/> on top of
/// <see cref="OrchestrationDbContext"/>: reads + writes are EF, the
/// <c>ClaimNextAsync</c> atomic pick is a guarded
/// <c>UPDATE ... FOR UPDATE SKIP LOCKED ... RETURNING</c> (mirrors the
/// <c>IWorkItemQueue</c> pattern, but the merge-queue is
/// operator-driven, not worker-driven, so the SQL is much shorter).
/// </summary>
/// <param name="db"></param>
public sealed class MergeQueueStoreEf(OrchestrationDbContext db) : IMergeQueueStore
{
    /// <inheritdoc />
    public async Task AddAsync(MergeQueueEntry entry, CancellationToken cancellationToken = default)
    {
        _ = db.MergeQueue.Add(entry);
        _ = await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task SaveAsync(MergeQueueEntry entry, CancellationToken cancellationToken = default)
    {
        _ = db.MergeQueue.Update(entry);
        _ = await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<MergeQueueEntry?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return db.MergeQueue
            .FirstOrDefaultAsync(entry => entry.Id == id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MergeQueueEntry>> ListAsync(
        MergeQueueStatus? status,
        ProjectId? projectId,
        int limit,
        int offset,
        CancellationToken cancellationToken = default)
    {
        var query = db.MergeQueue.AsNoTracking().AsQueryable();

        if (status is { } s)
        {
            query = query.Where(entry => entry.Status == s);
        }

        if (projectId is { } pid)
        {
            query = query.Where(entry => entry.ProjectId == pid);
        }

        var rows = await query
            .OrderBy(entry => entry.EnqueuedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return rows;
    }

    /// <inheritdoc />
    public async Task<MergeQueueEntry?> ClaimNextAsync(
        ProjectId? projectId,
        string operatorId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await using var command = MergeQueueStoreSql.CreateClaimNextCommand(
            transaction, projectId, operatorId, now);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var claimed = MergeQueueStoreSql.ReadClaimed(reader);
        await transaction.CommitAsync(cancellationToken);
        return claimed;
    }
}
