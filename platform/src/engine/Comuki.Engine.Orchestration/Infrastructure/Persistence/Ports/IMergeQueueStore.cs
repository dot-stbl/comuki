using Comuki.Engine.Orchestration.Domain.MergeQueue;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Engine.Orchestration.Infrastructure.Persistence.Ports;

/// <summary>
/// Persistence seam for the merge-queue. The application layer talks to
/// this port; the EF implementation owns the raw-SQL claim atomicity
/// (FOR UPDATE SKIP LOCKED on the pending set, same shape as
/// <c>IWorkItemQueue</c>). Scoped — same lifetime as the DbContext.
/// </summary>
public interface IMergeQueueStore
{
    /// <summary>Adds a new entry. The entry must be in <see cref="MergeQueueStatus.Pending"/>.</summary>
    /// <param name="entry"></param>
    /// <param name="cancellationToken"></param>
    public Task AddAsync(MergeQueueEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Persists an entry's mutated fields (status, claim, merge / abandon timestamps, notes).</summary>
    /// <param name="entry"></param>
    /// <param name="cancellationToken"></param>
    public Task SaveAsync(MergeQueueEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Returns the entry by id, or null when not in scope / not found.</summary>
    /// <param name="id"></param>
    /// <param name="cancellationToken"></param>
    public Task<MergeQueueEntry?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Paged listing — newest enqueues first within the optional status / project filter.</summary>
    /// <param name="status"></param>
    /// <param name="projectId"></param>
    /// <param name="limit"></param>
    /// <param name="offset"></param>
    /// <param name="cancellationToken"></param>
    public Task<IReadOnlyList<MergeQueueEntry>> ListAsync(
        MergeQueueStatus? status,
        ProjectId? projectId,
        int limit,
        int offset,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically claims the oldest pending entry in the requested scope
    /// (null project id spans every project): guarded UPDATE ... WHERE
    /// status='Pending' ORDER BY enqueued_at FOR UPDATE SKIP LOCKED
    /// LIMIT 1. Returns null when nothing is claimable.
    /// </summary>
    /// <param name="projectId"></param>
    /// <param name="operatorId"></param>
    /// <param name="now"></param>
    /// <param name="cancellationToken"></param>
    public Task<MergeQueueEntry?> ClaimNextAsync(
        ProjectId? projectId,
        string operatorId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
