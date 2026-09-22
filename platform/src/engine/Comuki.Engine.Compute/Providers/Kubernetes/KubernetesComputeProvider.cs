using Comuki.Engine.Compute.Options;
using Comuki.Shared.Contracts.Compute;
using Comuki.Shared.Kernel.Ids;
using k8s;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comuki.Engine.Compute.Providers.Kubernetes;

/// <summary>
/// Kubernetes implementation of <see cref="IComputeProvider"/> (prod): one
/// worker = one batch/v1 Job with <c>backoffLimit 0</c> and TTL cleanup,
/// label-selected listing and a coarse allocatable-based capacity hint. All
/// engine I/O goes through the injected <see cref="IKubernetes"/> (the
/// BatchV1/CoreV1 operation groups) so unit tests substitute them. No
/// real-cluster integration test exists by design — CI has no cluster;
/// e2e on kind is the slice DoD.
/// </summary>
/// <param name="services">
///     Composition root access — the provider resolves <see cref="IKubernetes"/>
///     through <c>GetService&lt;IKubernetes&gt;()</c> rather than constructor
///     injection because the registration may legally be null when the host
///     opts into <c>Compute:Kubernetes:SkipKubernetesConfig = true</c>
///     (DI cannot express nullable reference types as service types, so the
///     factory returns null and the provider consults the service provider).
///     In that mode the provider degrades to a no-op: empty list, zero
///     capacity, start/stop are logged and dropped (start throws a typed
///     <see cref="NotSupportedException"/> because silently dropping would
///     mask caller mistakes).
/// </param>
/// <param name="computeOptions">Kubernetes options bound from configuration.</param>
/// <param name="logger">Provider-scoped logger; used to surface no-op degradations.</param>
public sealed class KubernetesComputeProvider(
    IServiceProvider services,
    IOptions<KubernetesComputeOptions> computeOptions,
    ILogger<KubernetesComputeProvider> logger) : IComputeProvider
{
    /// <summary>
    /// Annotation carrying the <see cref="WorkerId"/> on the Job so list/stop
    /// map it back to the worker the orchestrator knows about. Annotation, not
    /// label: claim matching never selects by worker id, and the Job selector
    /// stays on the four comuki.* contract labels.
    /// </summary>
    public const string WorkerIdAnnotation = "comuki.worker_id";

    private IKubernetes? Kubernetes => services.GetService(typeof(IKubernetes)) as IKubernetes;

    /// <inheritdoc />
    public string Name => "kubernetes";

    /// <inheritdoc />
    public async Task<WorkerHandle> StartAsync(ComputeStartRequest request, CancellationToken cancellationToken = default)
    {
        var kubernetes = Kubernetes;
        if (kubernetes is null)
        {
            logger.LogWarning(
                "KubernetesComputeProvider.StartAsync invoked without a configured client "
                    + "(Compute:Kubernetes:SkipKubernetesConfig=true); refusing to start worker");
            throw new NotSupportedException(
                "Kubernetes compute provider is in no-op mode "
                    + "(Compute:Kubernetes:SkipKubernetesConfig=true); cannot start workers.");
        }

        var workerId = request.PreIssuedWorkerId ?? WorkerId.New();
        var job = KubernetesComputeMapping.ToJob(request, workerId, computeOptions.Value);

        var created = await kubernetes.BatchV1.CreateNamespacedJobAsync(
            job,
            computeOptions.Value.Namespace,
            cancellationToken: cancellationToken);

        return new WorkerHandle(workerId, created.Metadata.Name ?? KubernetesComputeMapping.ToJobName(workerId));
    }

    /// <inheritdoc />
    public async Task StopAsync(WorkerId workerId, ComputeStopReason reason, CancellationToken cancellationToken = default)
    {
        var kubernetes = Kubernetes;
        if (kubernetes is null)
        {
            logger.LogDebug(
                "KubernetesComputeProvider.StopAsync invoked without a configured client "
                    + "(Compute:Kubernetes:SkipKubernetesConfig=true); no-op");
            return;
        }

        var deleteOptions = KubernetesComputeMapping.ToDeleteOptions(reason, computeOptions.Value);
        try
        {
            await kubernetes.BatchV1.DeleteNamespacedJobAsync(
                KubernetesComputeMapping.ToJobName(workerId),
                computeOptions.Value.Namespace,
                deleteOptions,
                cancellationToken: cancellationToken);
        }
        catch (k8s.Autorest.HttpOperationException exception)
        {
            // Already TTL-collected — stopping an absent worker is a no-op,
            // mirroring the docker provider's empty-container-list path.
            if (exception.Response?.StatusCode != System.Net.HttpStatusCode.NotFound)
            {
                throw;
            }
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkerInfo>> ListAsync(ProjectId projectId, CancellationToken cancellationToken = default)
    {
        var kubernetes = Kubernetes;
        if (kubernetes is null)
        {
            return [];
        }

        var jobs = await kubernetes.BatchV1.ListNamespacedJobAsync(
            computeOptions.Value.Namespace,
            labelSelector: KubernetesComputeMapping.ToProjectLabelSelector(projectId),
            cancellationToken: cancellationToken);

        var workers = new List<WorkerInfo>();
        foreach (var job in jobs.Items)
        {
            // Finished Jobs linger until the TTL controller collects them;
            // only Jobs with an active pod are running workers.
            if (KubernetesComputeMapping.IsRunning(job) && KubernetesComputeMapping.ToWorkerInfo(job) is { } worker)
            {
                workers.Add(worker);
            }
        }

        return workers;
    }

    /// <inheritdoc />
    public async Task<ComputeCapacity> GetCapacityAsync(CancellationToken cancellationToken = default)
    {
        if (Kubernetes is not { } kubernetes)
        {
            return new ComputeCapacity(FreeSlots: 0, RunningWorkers: 0);
        }

        var nodes = await kubernetes.CoreV1.ListNodeAsync(cancellationToken: cancellationToken);
        var pods = await kubernetes.CoreV1.ListPodForAllNamespacesAsync(cancellationToken: cancellationToken);

        return KubernetesCapacityMath.ToCapacity(nodes.Items, pods.Items, computeOptions.Value);
    }
}
