using System.ComponentModel.DataAnnotations;

namespace Comuki.Engine.Compute.Options;

/// <summary>
/// Compute layer selection: which provider backs
/// <see cref="Shared.Contracts.Compute.IComputeProvider"/>. Both
/// providers are always registered; this switch picks the active one at
/// composition time, so swapping dev (docker) and prod (kubernetes) is one
/// config line.
/// </summary>
public sealed class ComputeOptions
{
    public const string SectionName = "Compute";

    /// <summary>Provider key of the Docker implementation (dev / compose).</summary>
    public const string DockerProvider = "docker";

    /// <summary>Provider key of the Kubernetes implementation (prod / batch-v1 Job).</summary>
    public const string KubernetesProvider = "kubernetes";

    /// <summary>Active provider: <c>docker</c> or <c>kubernetes</c>.</summary>
    [Required]
    [MinLength(1)]
    public string Provider { get; init; } = DockerProvider;
}
