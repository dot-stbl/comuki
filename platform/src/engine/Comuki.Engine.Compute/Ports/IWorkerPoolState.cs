using Comuki.Engine.Compute.Pool;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Engine.Compute.Ports;

/// <summary>
/// Read/heartbeat surface of the supervisor's worker pool registry. Claim
/// and heartbeat events (Orchestration slice, wired post-merge) mark workers
/// busy/idle through this port. The supervisor is the structural writer —
/// register/remove/sync live on the concrete <see cref="WorkerPoolState"/>.
/// </summary>
public interface IWorkerPoolState
{
    /// <summary>All pool-tracked workers of the project, idle and busy.</summary>
    /// <param name="projectId"></param>
    public IReadOnlyList<PoolWorker> List(ProjectId projectId);

    /// <summary>Marks the worker as holding a claimed work item; refreshes its activity time.</summary>
    /// <param name="workerId"></param>
    public void MarkBusy(WorkerId workerId);

    /// <summary>Marks the worker as finished (idle); refreshes its activity time.</summary>
    /// <param name="workerId"></param>
    public void MarkIdle(WorkerId workerId);

    /// <summary>Heartbeat: refreshes the activity time without changing the busy/idle flag.</summary>
    /// <param name="workerId"></param>
    public void Touch(WorkerId workerId);
}
