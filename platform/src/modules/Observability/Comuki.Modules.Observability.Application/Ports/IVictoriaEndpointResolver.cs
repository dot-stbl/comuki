namespace Comuki.Modules.Observability.Application.Ports;

/// <summary>
/// Resolves the runtime URL of the VictoriaLogs + VictoriaMetrics
/// endpoints the observability module reads from. The resolver is the
/// boundary between the typed <see cref="Options.ObservabilityOptions"/>
/// and the wire-level HTTP client: the concrete implementation
/// composes <c>VictoriaLogsUrl</c> / <c>VictoriaMetricsUrl</c> with the
/// deploy stack's docker-compose service names (or an env override for
/// local dev), and exposes a health probe so the host composition can
/// register it as an <c>IHealthCheck</c> without leaking the module's
/// internals. <see cref="ResolveLogsUrl"/> and
/// <see cref="ResolveMetricsUrl"/> return absolute URIs (scheme + host)
/// — they are NOT joined with a query path by the resolver; the
/// Refit client carries the path segments.
/// </summary>
public interface IVictoriaEndpointResolver
{
    /// <summary>Absolute URI to the VictoriaLogs <c>/select</c> base (no trailing path).</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Uri ResolveLogsUrl(CancellationToken cancellationToken = default);

    /// <summary>Absolute URI to the VictoriaMetrics <c>/api/v1</c> base (no trailing path).</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Uri ResolveMetricsUrl(CancellationToken cancellationToken = default);

    /// <summary>Lightweight liveness probe the host composition uses as an <c>IHealthCheck</c>.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A short human-readable status string (typically the version reported by <c>/health</c>).</returns>
    public Task<string> ProbeAsync(CancellationToken cancellationToken = default);
}
