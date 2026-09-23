using Comuki.Engine.Compute.Options;
using Comuki.Shared.Contracts.Compute;
using Comuki.Shared.Kernel.Ids;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Options;

namespace Comuki.Engine.Compute.Providers;

/// <summary>
/// Docker implementation of <see cref="IComputeProvider"/> (dev / compose).
/// All engine I/O goes through the injected container operations so unit
/// tests substitute it. The Kubernetes provider (prod) lives elsewhere.
/// Starts are fail-closed on the egress fence: <see cref="DockerEgressFence"/>
/// resolves (and refuses) the fenced network before any container is created.
/// </summary>
/// <param name="egressFence">Egress fence verifier (fenced network must exist and be internal).</param>
/// <param name="containers"></param>
/// <param name="providerOptions">Compute-wide options (AllowUnfencedEgress dev override).</param>
/// <param name="computeOptions"></param>
public sealed class DockerComputeProvider(
    DockerEgressFence egressFence,
    IContainerOperations containers,
    IOptions<ComputeOptions> providerOptions,
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
        var workerId = request.PreIssuedWorkerId ?? WorkerId.New();
        var networkMode = await egressFence.ResolveNetworkModeAsync(
            computeOptions.Value,
            providerOptions.Value.AllowUnfencedEgress,
            cancellationToken);
        var createParameters = DockerComputeMapping.ToCreateParameters(request, workerId, computeOptions.Value, networkMode);

        var created = await containers.CreateContainerAsync(createParameters, cancellationToken);
        await containers.StartContainerAsync(created.ID, new ContainerStartParameters(), cancellationToken);

        return new WorkerHandle(workerId, created.ID);
    }

    /// <inheritdoc />
    public async Task StopAsync(WorkerId workerId, ComputeStopReason reason, CancellationToken cancellationToken = default)
    {
        var matches = await containers.ListContainersAsync(
            DockerComputeMapping.ToWorkerListParameters(workerId), cancellationToken);

        foreach (var container in matches)
        {
            await containers.StopContainerAsync(
                container.ID,
                new ContainerStopParameters { WaitBeforeKillSeconds = (uint)computeOptions.Value.WaitBeforeKillSeconds },
                cancellationToken);
            await containers.RemoveContainerAsync(
                container.ID,
                new ContainerRemoveParameters { Force = true },
                cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkerInfo>> ListAsync(ProjectId projectId, CancellationToken cancellationToken = default)
    {
        var matches = await containers.ListContainersAsync(
            DockerComputeMapping.ToProjectListParameters(projectId), cancellationToken);

        return [.. matches.Select(DockerComputeMapping.ToWorkerInfo).OfType<WorkerInfo>()];
    }

    /// <inheritdoc />
    public async Task<ComputeCapacity> GetCapacityAsync(CancellationToken cancellationToken = default)
    {
        var matches = await containers.ListContainersAsync(
            DockerComputeMapping.ToWorkerListParameters(), cancellationToken);

        var runningWorkers = matches.Count;
        var freeSlots = Math.Max(0, computeOptions.Value.MaxWorkers - runningWorkers);
        return new ComputeCapacity(freeSlots, runningWorkers);
    }
}
