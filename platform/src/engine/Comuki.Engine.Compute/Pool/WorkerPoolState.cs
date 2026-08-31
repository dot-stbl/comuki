using System.Collections.Concurrent;
using Comuki.Engine.Compute.Ports;
using Comuki.Shared.Contracts.Compute;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Engine.Compute.Pool;

/// <summary>
/// In-memory worker pool registry maintained by the scale supervisor: handles
/// of started workers, activity marks from claims/heartbeats, and a reconcile
/// pass against <see cref="IComputeProvider.ListAsync"/> so workers left by a
/// previous process are adopted (idle, full TTL ahead) and gone ones dropped.
/// </summary>
/// <param name="computeProvider"></param>
/// <param name="clock"></param>
public sealed class WorkerPoolState(
    IComputeProvider computeProvider,
    TimeProvider clock) : IWorkerPoolState
{
    private readonly ConcurrentDictionary<WorkerId, PoolWorker> workers = new();

    /// <inheritdoc />
    public IReadOnlyList<PoolWorker> List(ProjectId projectId)
    {
        return [.. workers.Values.Where(worker => worker.ProjectId == projectId)];
    }

    /// <inheritdoc />
    public void MarkBusy(WorkerId workerId)
    {
        if (workers.TryGetValue(workerId, out var worker))
        {
            workers[workerId] = worker with { IsBusy = true, LastActiveAt = clock.GetUtcNow() };
        }
    }

    /// <inheritdoc />
    public void MarkIdle(WorkerId workerId)
    {
        if (workers.TryGetValue(workerId, out var worker))
        {
            workers[workerId] = worker with { IsBusy = false, LastActiveAt = clock.GetUtcNow() };
        }
    }

    /// <inheritdoc />
    public void Touch(WorkerId workerId)
    {
        if (workers.TryGetValue(workerId, out var worker))
        {
            workers[workerId] = worker with { LastActiveAt = clock.GetUtcNow() };
        }
    }

    /// <summary>Registers a freshly started worker as idle; its activity clock starts now.</summary>
    /// <param name="handle"></param>
    /// <param name="tokenId"></param>
    /// <param name="projectId"></param>
    /// <param name="profileKey"></param>
    public void Register(WorkerHandle handle, WorkerId tokenId, ProjectId projectId, string profileKey)
    {
        workers[handle.Id] = new PoolWorker(
            handle.Id,
            tokenId,
            projectId,
            profileKey,
            handle.ProviderRef,
            clock.GetUtcNow(),
            IsBusy: false);
    }

    /// <summary>Drops a worker from the registry — after a stop, or when the provider no longer lists it.</summary>
    /// <param name="workerId"></param>
    public void Remove(WorkerId workerId)
    {
        workers.TryRemove(workerId, out _);
    }

    /// <summary>
    /// Reconciles the cache with the provider: adopts unknown running workers
    /// (idle, activity = now, so adopted workers get a full TTL before
    /// reaping) and drops cached ones the provider no longer reports. Known
    /// workers keep their busy flag and activity time untouched.
    /// </summary>
    /// <param name="projectId"></param>
    /// <param name="cancellationToken"></param>
    public async Task SyncFromProviderAsync(ProjectId projectId, CancellationToken cancellationToken = default)
    {
        var listed = await computeProvider.ListAsync(projectId, cancellationToken);
        var listedIds = listed.Select(worker => worker.Id).ToHashSet();

        foreach (var cached in workers.Values.Where(worker => worker.ProjectId == projectId && !listedIds.Contains(worker.Id)))
        {
            workers.TryRemove(cached.Id, out _);
        }

        foreach (var info in listed.Where(worker => !workers.ContainsKey(worker.Id)))
        {
            workers[info.Id] = new PoolWorker(
                info.Id,
                info.Id,
                projectId,
                info.ProfileKey,
                info.ProviderRef,
                clock.GetUtcNow(),
                IsBusy: false);
        }
    }
}
