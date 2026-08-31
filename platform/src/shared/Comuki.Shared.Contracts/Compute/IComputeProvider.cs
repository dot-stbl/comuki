using Comuki.Shared.Kernel.Ids;

namespace Comuki.Shared.Contracts.Compute;

/// <summary>
/// Port to a container runtime. Implementations: Docker (dev/compose),
/// Kubernetes (prod — batch/v1 Job). Orchestration never talks to a runtime
/// SDK directly; the pool/scale policy lives on top of this port.
/// </summary>
public interface IComputeProvider
{
    /// <summary>Provider name: <c>docker</c>, <c>kubernetes</c>.</summary>
    public string Name { get; }

    /// <summary>
    /// Starts one worker container. The caller passes everything the container
    /// needs in the environment: worker token, project, profile ref, gRPC url.
    /// </summary>
    /// <param name="request"></param>
    /// <param name="cancellationToken"></param>
    public Task<WorkerHandle> StartAsync(ComputeStartRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops and removes the runtime behind <paramref name="workerId"/>.
    /// Soft stop of the running work is NOT this — that goes over gRPC first;
    /// this is the kill/drain path.
    /// </summary>
    /// <param name="workerId"></param>
    /// <param name="reason"></param>
    /// <param name="cancellationToken"></param>
    public Task StopAsync(WorkerId workerId, ComputeStopReason reason, CancellationToken cancellationToken = default);

    /// <summary>Running workers of a project (label-selected).</summary>
    /// <param name="projectId"></param>
    /// <param name="cancellationToken"></param>
    public Task<IReadOnlyList<WorkerInfo>> ListAsync(ProjectId projectId, CancellationToken cancellationToken = default);

    /// <summary>Capacity hint (allocatable) for quota-aware scale decisions.</summary>
    /// <param name="cancellationToken"></param>
    public Task<ComputeCapacity> GetCapacityAsync(CancellationToken cancellationToken = default);
}
