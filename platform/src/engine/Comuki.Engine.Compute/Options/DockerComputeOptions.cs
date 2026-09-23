using System.ComponentModel.DataAnnotations;

namespace Comuki.Engine.Compute.Options;

/// <summary>
/// Docker compute provider settings. The default network is <c>bridge</c> —
/// a compose deployment overrides it with the compose network name so worker
/// containers reach the orchestrator service directly.
/// </summary>
public sealed class DockerComputeOptions
{
    public const string SectionName = "Compute:Docker";

    /// <summary>Docker network the worker containers join.</summary>
    [Required]
    [MinLength(1)]
    public string NetworkMode { get; init; } = "bridge";

    /// <summary>
    /// Name of the INTERNAL Docker network used as the worker egress fence
    /// (deploy/compose defines <c>comuki-worker-net</c>). When set, the
    /// provider verifies the network exists and is internal before every
    /// start and places the worker container on it; a missing or
    /// non-internal network aborts the start (fail-closed) unless
    /// <c>Compute:AllowUnfencedEgress=true</c>. Null/empty = no fence
    /// configured — starts are refused under the same rule.
    /// </summary>
    public string? FencedNetwork { get; init; }

    /// <summary>Upper bound of concurrently running worker containers.</summary>
    [Range(1, 1000)]
    public int MaxWorkers { get; init; } = 8;

    /// <summary>Seconds between SIGTERM and SIGKILL when stopping a container.</summary>
    [Range(0, 600)]
    public int WaitBeforeKillSeconds { get; init; } = 10;

    /// <summary>Memory limit of one worker container, bytes (2 GiB default). A worker exceeding it is killed by the runtime.</summary>
    [Range(16L * 1024 * 1024, long.MaxValue)]
    public long MemoryBytes { get; init; } = 2L * 1024 * 1024 * 1024;

    /// <summary>CPU limit of one worker container in nanoCPUs (1 CPU = 1_000_000_000; default 1 CPU).</summary>
    [Range(100_000_000L, long.MaxValue)]
    public long NanoCpus { get; init; } = 1_000_000_000L;

    /// <summary>
    /// User the worker container runs as — uid (or uid:gid) string, non-root
    /// by default; must match the USER baked into the worker image
    /// (deploy/compose/docker/worker.Dockerfile).
    /// </summary>
    [Required]
    [MinLength(1)]
    public string RunAsUser { get; init; } = "1000";
}
