using System.Net;
using Comuki.Engine.Compute.Environments.Catalog;
using Comuki.Engine.Compute.Environments.Pinning;
using Comuki.Engine.Compute.Exceptions;
using Comuki.Engine.Compute.Options;
using Comuki.Shared.Contracts.Compute;
using Comuki.Shared.Kernel.Ids;
using k8s;
using k8s.Autorest;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comuki.Engine.Compute.Providers.Kubernetes;

/// <summary>
/// Kubernetes implementation of <see cref="IComputeProvider"/> (prod): one
/// worker = one batch/v1 Job with <c>backoffLimit 0</c> and TTL cleanup,
/// label-selected listing and a coarse allocatable-based capacity hint. All
/// engine I/O goes through the injected <see cref="IKubernetes"/> (the
/// BatchV1/CoreV1/NetworkingV1 operation groups) so unit tests substitute
/// them. No real-cluster integration test exists by design — CI has no
/// cluster; e2e on kind is the slice DoD. Starts are fail-closed on the
/// egress fence: the per-worker default-deny NetworkPolicy is created
/// before the Job, and a creation failure aborts the start
/// (<see cref="ComputeFenceException"/>) unless
/// <c>Compute:AllowUnfencedEgress=true</c>. Starts also resolve the
/// <see cref="ComputeStartRequest.EnvClass"/> through
/// <see cref="IEnvironmentCatalog"/> (mirroring the docker provider) so the
/// Production tag-only refusal fires uniformly on both providers.
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
/// <param name="environmentCatalog">Catalog of environment classes the start path resolves the bound class against.</param>
/// <param name="hostEnvironment">Host environment; the Production refusal reads <see cref="HostEnvironmentEnvExtensions.IsProduction(IHostEnvironment)"/>.</param>
/// <param name="providerOptions">Compute-wide options (AllowUnfencedEgress dev override).</param>
/// <param name="computeOptions">Kubernetes options bound from configuration.</param>
/// <param name="logger">Provider-scoped logger; used to surface no-op degradations.</param>
public sealed class KubernetesComputeProvider(
    IServiceProvider services,
    IEnvironmentCatalog environmentCatalog,
    IHostEnvironment hostEnvironment,
    IOptions<ComputeOptions> providerOptions,
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
        if (Kubernetes is not { } kubernetes)
        {
            logger.LogWarning(
                "KubernetesComputeProvider.StartAsync invoked without a configured client "
                    + "(Compute:Kubernetes:SkipKubernetesConfig=true); refusing to start worker");
            throw new NotSupportedException(
                "Kubernetes compute provider is in no-op mode "
                    + "(Compute:Kubernetes:SkipKubernetesConfig=true); cannot start workers.");
        }

        var workerId = request.PreIssuedWorkerId ?? WorkerId.New();

        // Resolve the class through the catalog before any k8s resource is created —
        // Production needs a digest-pinned reference, and the catalog is the one
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
        // §"Unallowlisted community bundle is not started"): mirrors the
        // docker provider — an unallowlisted publisher is refused before
        // any k8s resource (NetworkPolicy, Job) is created.
        if (!environmentCatalog.IsAllowed(bundle.Publisher.Value))
        {
            throw new InvalidOperationException(
                $"Environment class '{request.EnvClass}' publisher '{bundle.Publisher.Value}' is not on the fleet allowlist; refusing to start a worker.");
        }

        var resolvedRequest = request with { Image = bundle.Image };

        var policy = KubernetesComputeMapping.ToNetworkPolicy(resolvedRequest, workerId, computeOptions.Value);

        // The fence goes in before the Job: on failure no Job is created
        // (fail-closed) unless the unfenced dev override is set.
        try
        {
            await kubernetes.NetworkingV1.CreateNamespacedNetworkPolicyAsync(
                policy,
                computeOptions.Value.Namespace,
                cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (exception is HttpOperationException or HttpRequestException)
        {
            if (!providerOptions.Value.AllowUnfencedEgress)
            {
                throw new ComputeFenceException(
                    $"NetworkPolicy '{policy.Metadata.Name}' could not be created in namespace "
                        + $"'{computeOptions.Value.Namespace}'; refusing to start a worker without an egress fence. "
                        + "Grant the orchestrator RBAC on networking.k8s.io/networkpolicies "
                        + "or set Compute:AllowUnfencedEgress=true for development.",
                    exception);
            }

            logger.LogWarning(
                exception,
                "Worker starts unfenced: NetworkPolicy {PolicyName} creation failed (Compute:AllowUnfencedEgress=true)",
                policy.Metadata.Name);
        }

        return new WorkerHandle(
            workerId,
            (await kubernetes.BatchV1.CreateNamespacedJobAsync(
                KubernetesComputeMapping.ToJob(resolvedRequest, workerId, computeOptions.Value),
                computeOptions.Value.Namespace,
                cancellationToken: cancellationToken)).Metadata.Name
            ?? KubernetesComputeMapping.ToJobName(workerId));
    }

    /// <inheritdoc />
    public async Task StopAsync(WorkerId workerId, ComputeStopReason reason, CancellationToken cancellationToken = default)
    {
        if (Kubernetes is not { } kubernetes)
        {
            logger.LogDebug(
                "KubernetesComputeProvider.StopAsync invoked without a configured client "
                    + "(Compute:Kubernetes:SkipKubernetesConfig=true); no-op");
            return;
        }

        try
        {
            await kubernetes.BatchV1.DeleteNamespacedJobAsync(
                KubernetesComputeMapping.ToJobName(workerId),
                computeOptions.Value.Namespace,
                KubernetesComputeMapping.ToDeleteOptions(reason, computeOptions.Value),
                cancellationToken: cancellationToken);
        }
        catch (HttpOperationException exception)
        {
            // Already TTL-collected — stopping an absent worker is a no-op,
            // mirroring the docker provider's empty-container-list path.
            if (exception.Response?.StatusCode != HttpStatusCode.NotFound)
            {
                throw;
            }
        }

        // The per-worker egress fence dies with the Job; an absent policy
        // (already collected, never created under the unfenced override) is
        // a no-op with the same shape.
        try
        {
            await kubernetes.NetworkingV1.DeleteNamespacedNetworkPolicyAsync(
                KubernetesComputeMapping.ToNetworkPolicyName(workerId),
                computeOptions.Value.Namespace,
                cancellationToken: cancellationToken);
        }
        catch (HttpOperationException exception)
        {
            if (exception.Response?.StatusCode != HttpStatusCode.NotFound)
            {
                throw;
            }
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkerInfo>> ListAsync(ProjectId projectId, CancellationToken cancellationToken = default)
    {
        if (Kubernetes is not { } kubernetes)
        {
            return [];
        }

        // Finished Jobs linger until the TTL controller collects them;
        // only Jobs with an active pod are running workers.
        return [.. (await kubernetes.BatchV1.ListNamespacedJobAsync(
                computeOptions.Value.Namespace,
                labelSelector: KubernetesComputeMapping.ToProjectLabelSelector(projectId),
                cancellationToken: cancellationToken))
            .Items
            .Where(KubernetesComputeMapping.IsRunning)
            .Select(KubernetesComputeMapping.ToWorkerInfo)
            .OfType<WorkerInfo>()];
    }

    /// <inheritdoc />
    public async Task<ComputeCapacity> GetCapacityAsync(CancellationToken cancellationToken = default)
    {
        return Kubernetes is not { } kubernetes
            ? new ComputeCapacity(FreeSlots: 0, RunningWorkers: 0)
            : KubernetesCapacityMath.ToCapacity(
            (await kubernetes.CoreV1.ListNodeAsync(cancellationToken: cancellationToken)).Items,
            (await kubernetes.CoreV1.ListPodForAllNamespacesAsync(cancellationToken: cancellationToken)).Items,
            computeOptions.Value);
    }
}
