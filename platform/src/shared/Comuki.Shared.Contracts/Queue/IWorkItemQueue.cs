using Comuki.Shared.Kernel.Ids;

namespace Comuki.Shared.Contracts.Queue;

/// <summary>
/// Port to the work item queue — the claim/lease lifecycle. Implementations
/// are expected to guard every mutation by lease owner (and, where relevant,
/// unexpired lease) directly in the store, and to journal status transitions
/// in the same transaction. Misses are values (null / false), not exceptions.
/// </summary>
public interface IWorkItemQueue
{
    /// <summary>
    /// Claims the oldest queued item matching <paramref name="labels"/> and
    /// leases it to the worker until <paramref name="leaseUntil"/> (bumping the
    /// attempt counter). Returns null when nothing matching is queued.
    /// </summary>
    /// <param name="workerId"></param>
    /// <param name="labels"></param>
    /// <param name="leaseUntil"></param>
    /// <param name="now"></param>
    /// <param name="cancellationToken"></param>
    public Task<ClaimedWorkItem?> ClaimAsync(
        WorkerId workerId,
        WorkItemLabels labels,
        DateTimeOffset leaseUntil,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extends the lease of an item the worker owns. Returns false when the
    /// item is unknown, not running, not leased to this worker, or the lease
    /// already expired (the reaper owns it from then on).
    /// </summary>
    /// <param name="workItemId"></param>
    /// <param name="workerId"></param>
    /// <param name="leaseUntil"></param>
    /// <param name="now"></param>
    /// <param name="cancellationToken"></param>
    public Task<bool> HeartbeatAsync(
        Guid workItemId,
        WorkerId workerId,
        DateTimeOffset leaseUntil,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes an item the worker owns (Running -> Succeeded) with the
    /// worker's result JSON, releasing the lease. Returns false when the
    /// worker does not own a running item with this id.
    /// </summary>
    /// <param name="workItemId"></param>
    /// <param name="workerId"></param>
    /// <param name="resultJson"></param>
    /// <param name="now"></param>
    /// <param name="cancellationToken"></param>
    public Task<bool> CompleteAsync(
        Guid workItemId,
        WorkerId workerId,
        string resultJson,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fails an item the worker owns (Running -> Failed) with a reason,
    /// releasing the lease. Returns false when the worker does not own a
    /// running item with this id.
    /// </summary>
    /// <param name="workItemId"></param>
    /// <param name="workerId"></param>
    /// <param name="reason"></param>
    /// <param name="now"></param>
    /// <param name="cancellationToken"></param>
    public Task<bool> FailAsync(
        Guid workItemId,
        WorkerId workerId,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>Count of queued items, optionally scoped to one profile key.</summary>
    /// <param name="profileKey"></param>
    /// <param name="cancellationToken"></param>
    public Task<int> CountQueuedAsync(string? profileKey = null, CancellationToken cancellationToken = default);
}
