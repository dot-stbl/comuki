using Comuki.Engine.Orchestration.Domain.MergeQueue;

namespace Comuki.Engine.Orchestration.Infrastructure.Persistence.Ports;

/// <summary>
/// Persistence seam for merge batches. The application layer talks to
/// this port; the EF implementation owns the batch table. Scoped — same
/// lifetime as the DbContext.
/// </summary>
public interface IMergeBatchStore
{
    /// <summary>Adds a new batch. The batch must be in <see cref="MergeBatchStatus.Pending"/>.</summary>
    /// <param name="batch"></param>
    /// <param name="cancellationToken"></param>
    public Task AddAsync(MergeBatch batch, CancellationToken cancellationToken = default);

    /// <summary>Persists a batch's mutated fields (status, merge / abandon timestamps).</summary>
    /// <param name="batch"></param>
    /// <param name="cancellationToken"></param>
    public Task SaveAsync(MergeBatch batch, CancellationToken cancellationToken = default);

    /// <summary>Returns the batch by id, or null when not in scope / not found.</summary>
    /// <param name="id"></param>
    /// <param name="cancellationToken"></param>
    public Task<MergeBatch?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Paged listing — newest batches first within the optional status filter.</summary>
    /// <param name="status"></param>
    /// <param name="limit"></param>
    /// <param name="offset"></param>
    /// <param name="cancellationToken"></param>
    public Task<IReadOnlyList<MergeBatch>> ListAsync(
        MergeBatchStatus? status,
        int limit,
        int offset,
        CancellationToken cancellationToken = default);
}
