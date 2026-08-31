using Comuki.Shared.Kernel.Ids;

namespace Comuki.Engine.Compute.Ports;

/// <summary>
/// Local port counting queued work items — the scale supervisor's backlog
/// signal. TEMPORARY SEAM: the real queue (IWorkItemQueue) is being built in
/// Engine.Orchestration by a sibling slice; when both merge, an adapter
/// implements this port on top of the real queue and this local interface
/// collapses. Until then hosts must register one implementation (fakes in
/// tests) or the supervisor's DI resolution fails fast.
/// </summary>
public interface IBacklogReader
{
    /// <summary>Counts queued (claimable) work items of a project, optionally filtered to one profile key.</summary>
    /// <param name="projectId"></param>
    /// <param name="profileKey"></param>
    /// <param name="cancellationToken"></param>
    public Task<int> CountQueuedAsync(ProjectId projectId, string? profileKey, CancellationToken cancellationToken = default);
}
