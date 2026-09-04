using Comuki.Modules.Proxy.Application.Options;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Comuki.Host.HealthChecks;

/// <summary>
/// Surfaces a 200 / Unhealthy verdict based on whether the proxy module
/// has any configured virtual keys. When the proxy is disabled the
/// check is silently healthy — operators may not run the proxy at all
/// in dev / staging, and a missing-key warning would be noise.
/// </summary>
public sealed class ProxyKeysHealthCheck(IOptionsMonitor<ProxyOptions> options) : IHealthCheck
{
    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var snapshot = options.CurrentValue;
        return !snapshot.Enabled
            ? Task.FromResult(HealthCheckResult.Healthy(description: "Proxy is disabled"))
            : snapshot.VirtualKeys.Count == 0
                ? Task.FromResult(HealthCheckResult.Unhealthy(description: "Proxy:Enabled is true but VirtualKeys is empty"))
                : Task.FromResult(HealthCheckResult.Healthy(description: $"Proxy:Enabled with {snapshot.VirtualKeys.Count} virtual key(s)"));
    }
}
