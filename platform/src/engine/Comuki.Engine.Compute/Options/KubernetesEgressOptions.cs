namespace Comuki.Engine.Compute.Options;

/// <summary>
/// Egress fence settings of the Kubernetes provider, bound as the
/// <c>Compute:Kubernetes:Egress</c> sub-section of
/// <see cref="KubernetesComputeOptions"/>. v1 carries only the CIDR
/// allowlist the per-worker default-deny NetworkPolicy opens next to
/// cluster DNS; FQDN allowlisting lands with SourceGitUrl.
/// </summary>
public sealed class KubernetesEgressOptions
{
    /// <summary>
    /// CIDRs a worker may reach besides cluster DNS — operators put the
    /// model-proxy / Nexus / orchestrator service range here. Empty means
    /// DNS-only egress (everything else denied).
    /// </summary>
    public IReadOnlyList<string> AllowedCidrs { get; init; } = [];
}
