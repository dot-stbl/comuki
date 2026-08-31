using Comuki.Shared.Kernel.Ids;

namespace Comuki.Engine.Compute.Security.Stores;

/// <summary>
/// Persistence for worker token records. V1 ships the in-memory store; the
/// DB-backed store arrives with the Identity slice and must keep the same
/// contract (replace-on-save semantics, revoke by worker).
/// </summary>
public interface IWorkerTokenStore
{
    /// <summary>Saves the record of a worker, replacing any previous one.</summary>
    /// <param name="record"></param>
    public void Save(WorkerTokenRecord record);

    /// <summary>Removes the record of a worker. No-op when absent.</summary>
    /// <param name="workerId"></param>
    public void Revoke(WorkerId workerId);

    /// <summary>Snapshot of the stored records, for validation scans.</summary>
    public IReadOnlyList<WorkerTokenRecord> List();
}
