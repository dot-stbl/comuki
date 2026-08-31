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

    /// <summary>Upper bound of concurrently running worker containers.</summary>
    [Range(1, 1000)]
    public int MaxWorkers { get; init; } = 8;

    /// <summary>Seconds between SIGTERM and SIGKILL when stopping a container.</summary>
    [Range(0, 600)]
    public int WaitBeforeKillSeconds { get; init; } = 10;
}
