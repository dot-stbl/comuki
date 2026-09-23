namespace Comuki.Engine.Compute.Egress;

/// <summary>
/// Pure egress-allowlist resolution (worker-sandbox spec — "Effective
/// egress is profile intersect project"). The effective allowlist starts
/// from the deployment default (model proxy, package registry, git host)
/// and may only be NARROWED: by the project allowlist, then by the
/// profile allowlist. Nothing a caller, brief or Brain supplies can widen
/// it — a host outside the deployment default is dropped even when a
/// profile names it. No I/O; the provider options and (later) the
/// project/profile settings feed this function.
/// </summary>
internal static class EgressAllowlist
{
    /// <summary>
    /// Resolves the effective allowlist: deployment default ∩ project ∩
    /// profile. An empty or null project/profile list means "no narrowing"
    /// (that level keeps the broader set); an empty deployment default
    /// yields an empty allowlist — no egress at all (fail-closed).
    /// </summary>
    /// <param name="deploymentHosts">Deployment-default hosts (proxy, registry, git host) from provider options.</param>
    /// <param name="projectHosts">Hosts the project allows; null/empty = keep the deployment default.</param>
    /// <param name="profileHosts">Hosts the profile allows; null/empty = keep the project-level set.</param>
    /// <returns>Effective hosts in deployment-default order, without duplicates introduced by the narrowing lists.</returns>
    public static IReadOnlyList<string> Resolve(
        IReadOnlyCollection<string> deploymentHosts,
        IReadOnlyCollection<string>? projectHosts,
        IReadOnlyCollection<string>? profileHosts)
    {
        var effective = deploymentHosts.ToList();

        if (projectHosts is { Count: > 0 })
        {
            var allowedByProject = new HashSet<string>(projectHosts, StringComparer.OrdinalIgnoreCase);
            effective.RemoveAll(host => !allowedByProject.Contains(host));
        }

        if (profileHosts is { Count: > 0 })
        {
            var allowedByProfile = new HashSet<string>(profileHosts, StringComparer.OrdinalIgnoreCase);
            effective.RemoveAll(host => !allowedByProfile.Contains(host));
        }

        return effective;
    }
}
