using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Comuki.Host.HealthChecks;

/// <summary>
/// Health checks the orchestrator host registers (issue #8 cross-cutting
/// kit). Each probe is intentionally minimal — a liveness TCP/SELECT check
/// for Postgres, a HEAD probe to the MinIO health endpoint, and a
/// static-row check on the proxy virtual-key catalogue so the operator
/// notices a misconfigured proxy before traffic arrives. Failures log a
/// structured warning; the registered
/// <see cref="HealthStatus"/> drives the response shape.
/// </summary>
public static class ComukiHealthChecks
{
    /// <summary>Names registered for <see cref="HealthCheckRegistration"/>.</summary>
    public static class Names
    {
        /// <summary>Postgres TCP/Select-1 readiness.</summary>
        public const string Postgres = "comuki.postgres";

        /// <summary>Proxy virtual-key catalogue — non-empty when the proxy is enabled.</summary>
        public const string ProxyKeys = "comuki.proxy.keys";
    }
}
