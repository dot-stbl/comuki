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
    /// (<see cref="ComputeStopReason.IdleTtl"/>, <see cref="ComputeStopReason.Draining"/>,
    /// <see cref="ComputeStopReason.LeaseExpired"/>); <see cref="ComputeStopReason.Force"/>
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

    /// <summary>Optional nodeSelector pinned on the worker pod template.</summary>
    public IReadOnlyDictionary<string, string> NodeSelector { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
