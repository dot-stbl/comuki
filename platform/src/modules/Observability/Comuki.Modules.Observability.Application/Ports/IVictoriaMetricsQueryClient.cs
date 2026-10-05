using Comuki.Modules.Observability.Domain.Metrics;

namespace Comuki.Modules.Observability.Application.Ports;

/// <summary>
/// Read-only VictoriaMetrics HTTP surface. The interface methods return
/// typed DTOs (<see cref="MetricsQuery"/> → <see cref="MetricSeries"/>),
/// not raw <c>JsonElement</c>; the wire-shape mapping is the
/// infrastructure project's job. The concrete implementation behind
/// this port is a Refit client over the VictoriaMetrics Prometheus
/// HTTP API (<c>/api/v1/query</c> + <c>/api/v1/query_range</c> +
/// <c>/api/v1/series</c>).
/// </summary>
public interface IVictoriaMetricsQueryClient
{
    /// <summary>
    /// PromQL query. The MCP tool <c>observability.metrics.query</c>
    /// projects its <c>{promql, from, to}</c> JSON body onto this
    /// method.
    /// </summary>
    /// <param name="query">PromQL expression + time window + optional step.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matched time series with their samples.</returns>
    /// <exception cref="Domain.VictoriaUnavailableException">
    /// The configured VictoriaMetrics endpoint is unreachable for the full timeout.
    /// </exception>
    public Task<IReadOnlyList<MetricSeries>> QueryAsync(MetricsQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Label-set lookup by selector. The MCP tool
    /// <c>observability.metrics.series</c> projects its
    /// <c>{labelSelector}</c> JSON body onto this method.
    /// </summary>
    /// <param name="labelSelector">A Prometheus label selector (e.g. <c>job="comuki-orchestrator"</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The set of label keys + their distinct values for series matching the selector.</returns>
    /// <exception cref="Domain.VictoriaUnavailableException">
    /// The configured VictoriaMetrics endpoint is unreachable for the full timeout.
    /// </exception>
    public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> SeriesAsync(string labelSelector, CancellationToken cancellationToken = default);
}
