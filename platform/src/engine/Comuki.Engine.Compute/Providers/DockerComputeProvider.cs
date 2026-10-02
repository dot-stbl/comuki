using Comuki.Engine.Compute.Environments.Catalog;
using Comuki.Engine.Compute.Environments.Pinning;
using Comuki.Engine.Compute.Options;
using Comuki.Shared.Contracts.Compute;
using Comuki.Shared.Kernel.Ids;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Comuki.Engine.Compute.Providers;

/// <summary>
/// Docker implementation of <see cref="IComputeProvider"/> (dev / compose).
/// All engine I/O goes through the injected container operations so unit
/// tests substitute it. The Kubernetes provider (prod) lives elsewhere.
/// Starts are fail-closed on the egress fence: <see cref="DockerEgressFence"/>
/// resolves (and refuses) the fenced network before any container is created.
/// Starts also resolve the worker's <see cref="ComputeStartRequest.EnvClass"/>
/// through <see cref="IEnvironmentCatalog"/>: the catalog map drives both the
/// stamped image (a <c>comuki.env_class</c> may NOT name an image of a
/// different class — spec §"Project cannot swap class via image override")
/// and the Production tag-only refusal (<see cref="EnvironmentBundlePinning.IsStartable"/>).
/// </summary>
/// <param name="egressFence">Egress fence verifier (fenced network must exist and be internal).</param>
/// <param name="containers">Docker container operations — create, start, list, stop, remove.</param>
/// <param name="environmentCatalog">Catalog of environment classes the start path resolves the bound class against.</param>
/// <param name="hostEnvironment">Host environment; the Production refusal reads <see cref="HostEnvironmentEnvExtensions.IsProduction(IHostEnvironment)"/>.</param>
/// <param name="providerOptions">Compute-wide options (AllowUnfencedEgress dev override).</param>
/// <param name="computeOptions">Docker options bound from Compute:Docker — fenced network, RunAsUser, memory/cpu limits.</param>
public sealed class DockerComputeProvider(
    DockerEgressFence egressFence,
    IContainerOperations containers,
    IEnvironmentCatalog environmentCatalog,
    IHostEnvironment hostEnvironment,
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

        // Resolve the class through the catalog before the container is created
        // — Production needs a digest-pinned reference (worker-environments spec
        // §"Production refuses a tag-only bundle"), and the catalog is the one
        // place that knows what the bound class maps to. The provider stamps the
        // resolved image (not a caller-supplied one) so a per-project legacy image
        // override cannot silently swap the class.
        if (!environmentCatalog.TryGet(request.EnvClass, out var bundle) || bundle is null)
        {
            throw new InvalidOperationException(
                $"Environment class '{request.EnvClass}' is not in the catalog; refusing to start a worker.");
        }

        if (!EnvironmentBundlePinning.IsStartable(bundle.Image, hostEnvironment.IsProduction()))
        {
            throw new InvalidOperationException(
                $"Environment class '{request.EnvClass}' image '{bundle.Image}' is not startable "
                + $"(Production requires a digest; got a tag-only or untagged reference).");
        }

        // Fleet allowlist gate (worker-environments spec
        // §"Unallowlisted community bundle is not started"): the publisher
        // shelf must be on the fleet allowlist; the catalog refuses the
        // start before any container is created.
        if (!environmentCatalog.IsAllowed(bundle.Publisher.Value))
        {
            throw new InvalidOperationException(
                $"Environment class '{request.EnvClass}' publisher '{bundle.Publisher.Value}' is not on the fleet allowlist; refusing to start a worker.");
        }

        var resolvedRequest = request with { Image = bundle.Image };

        var created = await containers.CreateContainerAsync(
            DockerComputeMapping.ToCreateParameters(
                resolvedRequest,
                workerId,
                computeOptions.Value,
                await egressFence.ResolveNetworkModeAsync(
                    computeOptions.Value,
                    providerOptions.Value.AllowUnfencedEgress,
                    cancellationToken)),
            cancellationToken);
        await containers.StartContainerAsync(created.ID, new ContainerStartParameters(), cancellationToken);

        return new WorkerHandle(workerId, created.ID);
    }

    /// <inheritdoc />
    public async Task StopAsync(WorkerId workerId, ComputeStopReason reason, CancellationToken cancellationToken = default)
    {
        foreach (var container in await containers.ListContainersAsync(
                     DockerComputeMapping.ToWorkerListParameters(workerId), cancellationToken))
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
        return [.. (await containers.ListContainersAsync(
                DockerComputeMapping.ToProjectListParameters(projectId), cancellationToken))
            .Select(DockerComputeMapping.ToWorkerInfo)
            .OfType<WorkerInfo>()];
    }

    /// <inheritdoc />
    public async Task<ComputeCapacity> GetCapacityAsync(CancellationToken cancellationToken = default)
    {
        var runningWorkers = (await containers.ListContainersAsync(
            DockerComputeMapping.ToWorkerListParameters(), cancellationToken)).Count;

        return new ComputeCapacity(Math.Max(0, computeOptions.Value.MaxWorkers - runningWorkers), runningWorkers);
    }
}
