using Comuki.Engine.Orchestration.Domain.MergeQueue;
using Comuki.Engine.Orchestration.Infrastructure.Persistence.Ports;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Engine.Orchestration.Infrastructure.Persistence.Stores;

/// <summary>
/// Postgres implementation of <see cref="IMergeBatchStore"/> on top of
/// <see cref="OrchestrationDbContext"/>. Read + write + list are plain
/// EF — batches do not need the atomic claim path <c>IMergeQueueStore</c>
/// uses (operators drive claim via <see cref="MergeBatch.Claim"/> + a
/// regular save), so there is no raw-SQL branch here.
/// </summary>
/// <param name="db"></param>
public sealed class MergeBatchStoreEf(OrchestrationDbContext db) : IMergeBatchStore
{
    /// <inheritdoc />
    public async Task AddAsync(MergeBatch batch, CancellationToken cancellationToken = default)
    {
        db.MergeBatches.Add(batch);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task SaveAsync(MergeBatch batch, CancellationToken cancellationToken = default)
    {
        db.MergeBatches.Update(batch);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<MergeBatch?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return db.MergeBatches
            .FirstOrDefaultAsync(batch => batch.Id == id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MergeBatch>> ListAsync(
        MergeBatchStatus? status,
        int limit,
        int offset,
        CancellationToken cancellationToken = default)
    {
        var query = db.MergeBatches.AsNoTracking().AsQueryable();

        if (status is { } s)
        {
            query = query.Where(batch => batch.Status == s);
        }

        // IMergeBatchStore.ListAsync documents "newest batches first" — the
        // opposite direction from IMergeQueueStore.ListAsync (which is FIFO
        // by EnqueuedAt). Mirrored by ix_merge_batches_status_created_at.
        return await query
            .OrderByDescending(static batch => batch.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}
