using Comuki.Engine.Compute.Options;
using Comuki.Shared.Contracts.Compute;
using Comuki.Shared.Kernel.Ids;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Options;

namespace Comuki.Engine.Compute.Providers;

/// <summary>
/// Docker implementation of <see cref="IComputeProvider"/> (dev / compose).
/// All engine I/O goes through the injected <see cref="IDockerClient"/> so
/// unit tests substitute it. The Kubernetes provider (prod) lives elsewhere.
/// </summary>
/// <param name="docker"></param>
/// <param name="computeOptions"></param>
public sealed class DockerComputeProvider(
    IDockerClient docker,
    IOptions<DockerComputeOptions> computeOptions) : IComputeProvider
{
    /// <summary>
    /// Label carrying the <see cref="WorkerId"/> on a container so list/stop can map
    /// it back to the worker the orchestrator knows about. Provider-local — not part
    /// of <see cref="ComputeLabels"/> (that is the cross-provider claim-matching set).
    /// </summary>
    public const string WorkerIdLabel = "comuki.worker_id";

    /// <inheritdoc />
    public string Name => "docker";

    /// <inheritdoc />
    public async Task<WorkerHandle> StartAsync(ComputeStartRequest request, CancellationToken cancellationToken = default)
    {
        var workerId = WorkerId.New();
        var createParameters = DockerComputeMapping.ToCreateParameters(request, workerId, computeOptions.Value);

        var created = await docker.Containers.CreateContainerAsync(createParameters, cancellationToken);
        await docker.Containers.StartContainerAsync(created.ID, new ContainerStartParameters(), cancellationToken);

        return new WorkerHandle(workerId, created.ID);
    }

    /// <inheritdoc />
    public async Task StopAsync(WorkerId workerId, ComputeStopReason reason, CancellationToken cancellationToken = default)
    {
        var containers = await docker.Containers.ListContainersAsync(
            DockerComputeMapping.ToWorkerListParameters(workerId), cancellationToken);

        foreach (var container in containers)
        {
            await docker.Containers.StopContainerAsync(
                container.ID,
                new ContainerStopParameters { WaitBeforeKillSeconds = (uint)computeOptions.Value.WaitBeforeKillSeconds },
                cancellationToken);
            await docker.Containers.RemoveContainerAsync(
                container.ID,
                new ContainerRemoveParameters { Force = true },
                cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkerInfo>> ListAsync(ProjectId projectId, CancellationToken cancellationToken = default)
    {
        var containers = await docker.Containers.ListContainersAsync(
            DockerComputeMapping.ToProjectListParameters(projectId), cancellationToken);

        return [.. containers.Select(DockerComputeMapping.ToWorkerInfo).OfType<WorkerInfo>()];
    }

    /// <inheritdoc />
    public async Task<ComputeCapacity> GetCapacityAsync(CancellationToken cancellationToken = default)
    {
        var containers = await docker.Containers.ListContainersAsync(
            DockerComputeMapping.ToWorkerListParameters(), cancellationToken);

        var runningWorkers = containers.Count;
        var freeSlots = Math.Max(0, computeOptions.Value.MaxWorkers - runningWorkers);
        return new ComputeCapacity(freeSlots, runningWorkers);
    }
}
