using System.ComponentModel.DataAnnotations;

namespace Comuki.Engine.Compute.Options;

/// <summary>
/// Kubernetes compute provider settings. A worker runs as a batch/v1 Job
/// (<c>backoffLimit 0</c> — a failed worker is not retried by the platform,
/// the queue re-claims the item) cleaned up by the TTL controller.
/// </summary>
public sealed class KubernetesComputeOptions
{
    public const string SectionName = "Compute:Kubernetes";

    /// <summary>Namespace the worker Jobs are created in.</summary>
    [Required]
    [MinLength(1)]
    public string Namespace { get; init; } = "comuki";

    /// <summary>ServiceAccount the worker pod runs as (gRPC to the orchestrator only).</summary>
    [Required]
    [MinLength(1)]
    public string ServiceAccount { get; init; } = "comuki-worker";

    /// <summary>ttlSecondsAfterFinished — how long a finished Job lingers before the TTL controller deletes it.</summary>
    [Range(0, 2_592_000)]
    public int TtlSecondsAfterFinished { get; init; } = 600;

    /// <summary>
    /// Grace seconds passed to the Job delete for soft stop reasons
    /// (<see cref="Shared.Contracts.Compute.ComputeStopReason.IdleTtl"/>,
    /// <see cref="Shared.Contracts.Compute.ComputeStopReason.Draining"/>,
    /// <see cref="Shared.Contracts.Compute.ComputeStopReason.LeaseExpired"/>);
    /// <see cref="Shared.Contracts.Compute.ComputeStopReason.Force"/>
    /// always deletes with grace 0.
    /// </summary>
    [Range(0, 600)]
    public int TerminationGraceSeconds { get; init; } = 10;

    /// <summary>CPU request of one worker container, millicores — also the capacity-slot denominator.</summary>
    [Range(50, 64000)]
    public int CpuRequestMillis { get; init; } = 500;

    /// <summary>Memory request of one worker container, MiB — also the capacity-slot denominator.</summary>
    [Range(16, int.MaxValue)]
    public int MemoryRequestMiB { get; init; } = 1024;

    /// <summary>CPU limit of one worker container, millicores. Defaults to 2× <see cref="CpuRequestMillis"/>.</summary>
    [Range(50, 64000)]
    public int CpuLimitMillis { get; init; } = 1000;

    /// <summary>Memory limit of one worker container, MiB. Defaults to 2× <see cref="MemoryRequestMiB"/>.</summary>
    [Range(16, int.MaxValue)]
    public int MemoryLimitMiB { get; init; } = 2048;

    /// <summary>Optional nodeSelector pinned on the worker pod template.</summary>
    public IReadOnlyDictionary<string, string> NodeSelector { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    ///     Egress fence settings (<c>Compute:Kubernetes:Egress</c>): the
    ///     CIDR allowlist of the per-worker default-deny NetworkPolicy.
    /// </summary>
    public KubernetesEgressOptions Egress { get; init; } = new();

    /// <summary>
    ///     Path to an external kubeconfig file. When set, the Kubernetes client
    ///     reads this file instead of the in-cluster service account — used
    ///     when workers go to a separate cluster (e.g. vega) while the host
    ///     runs elsewhere. Empty/null = in-cluster (the default when the host
    ///     itself runs inside the target cluster).
    /// </summary>
    public string? KubeconfigPath { get; init; }

    /// <summary>
    ///     When true and <see cref="KubeconfigPath"/> is empty/whitespace, the
    ///     factory skips in-cluster initialisation entirely and returns null —
    ///     the installer wires the <c>IKubernetes</c> service to no-op so DI
    ///     resolves. Intended for hosts that load the compute engine for
    ///     composition symmetry but never actually call the Kubernetes
    ///     provider at runtime (e.g. integration fixtures, hosts pinned to
    ///     <c>Compute:Provider=docker</c> via env on a pod whose ServiceAccount
    ///     token is intentionally not mounted).
    /// </summary>
    public bool SkipKubernetesConfig { get; init; }
}
