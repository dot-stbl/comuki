using Comuki.Engine.Compute.Options;
using Comuki.Shared.Contracts.Compute;
using Comuki.Shared.Kernel.Ids;
using k8s;
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
/// <param name="kubernetes"></param>
/// <param name="computeOptions"></param>
public sealed class KubernetesComputeProvider(
    IKubernetes kubernetes,
    IOptions<KubernetesComputeOptions> computeOptions) : IComputeProvider
{
    /// <summary>
    /// Annotation carrying the <see cref="WorkerId"/> on the Job so list/stop
    /// map it back to the worker the orchestrator knows about. Annotation, not
    /// label: claim matching never selects by worker id, and the Job selector
    /// stays on the four comuki.* contract labels.
    /// </summary>
    public const string WorkerIdAnnotation = "comuki.worker_id";

    /// <inheritdoc />
    public string Name => "kubernetes";

    /// <inheritdoc />
    public async Task<WorkerHandle> StartAsync(ComputeStartRequest request, CancellationToken cancellationToken = default)
    {
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
        var nodes = await kubernetes.CoreV1.ListNodeAsync(cancellationToken: cancellationToken);
        var pods = await kubernetes.CoreV1.ListPodForAllNamespacesAsync(cancellationToken: cancellationToken);

        return KubernetesCapacityMath.ToCapacity(nodes.Items, pods.Items, computeOptions.Value);
    }
}
