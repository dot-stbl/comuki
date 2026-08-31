using System.Collections.Concurrent;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Engine.Compute.Security.Stores;

/// <summary>
/// In-memory <see cref="IWorkerTokenStore"/> — one live token per worker.
/// Process-local: issued tokens die with the orchestrator restart, which the
/// short token TTL makes acceptable for v1 (the DB-backed store comes with
/// the Identity slice).
/// </summary>
public sealed class InMemoryWorkerTokenStore() : IWorkerTokenStore
{
    private readonly ConcurrentDictionary<WorkerId, WorkerTokenRecord> recordsByWorker = new();

    /// <inheritdoc />
    public void Save(WorkerTokenRecord record)
    {
        recordsByWorker[record.WorkerId] = record;
    }

    /// <inheritdoc />
    public void Revoke(WorkerId workerId)
    {
        _ = recordsByWorker.TryRemove(workerId, out _);
    }

    /// <inheritdoc />
    public IReadOnlyList<WorkerTokenRecord> List()
    {
        return [.. recordsByWorker.Values];
    }
}
