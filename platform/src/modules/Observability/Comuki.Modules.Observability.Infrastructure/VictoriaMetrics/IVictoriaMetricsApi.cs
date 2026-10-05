using Refit;

namespace Comuki.Modules.Observability.Infrastructure.VictoriaMetrics;

/// <summary>
/// VictoriaMetrics Prometheus-compatible HTTP surface. The Refit client
/// uses base URLs the
/// <see cref="Application.Ports.IVictoriaEndpointResolver"/>
/// hands the factory (compose's <c>victoria-metrics</c> on
/// <see cref="Endpoint.VictoriaPorts.MetricsContainerPort"/>). The wire
/// shape mirrors the Prometheus HTTP API: <c>/api/v1/query</c> and
/// <c>/api/v1/query_range</c> return <see cref="PrometheusResponseEnvelope{TWire}"/>,
/// and <c>/api/v1/series</c> returns a flat <see cref="SeriesMatchWire"/>
/// array. The client maps each to the typed
/// <see cref="Domain.Metrics.MetricSeries"/>
/// / label-set shapes.
/// </summary>
internal interface IVictoriaMetricsApi
{
    /// <summary>PromQL instant query.</summary>
    /// <param name="query">URL-encoded PromQL expression (Refit escapes it for us).</param>
    /// <param name="time">Optional unix-ms evaluation timestamp.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The wire-shaped Prometheus response envelope.</returns>
    [Get("/api/v1/query")]
    public Task<PrometheusResponseEnvelope<PrometheusValueWire>> QueryAsync(
        [Query] string query,
        [Query] string? time = null,
        CancellationToken cancellationToken = default);

    /// <summary>PromQL range query (matrix).</summary>
    /// <param name="query">URL-encoded PromQL expression.</param>
    /// <param name="start">unix-ms inclusive lower bound.</param>
    /// <param name="end">unix-ms inclusive upper bound.</param>
    /// <param name="step">Resolution of the returned matrix (unix-ms).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The wire-shaped Prometheus response envelope.</returns>
    [Get("/api/v1/query_range")]
    public Task<PrometheusResponseEnvelope<PrometheusValueWire>> QueryRangeAsync(
        [Query] string query,
        [Query] string start,
        [Query] string end,
        [Query] string step,
        CancellationToken cancellationToken = default);

    /// <summary>Series / label-set lookup.</summary>
    /// <param name="match">A label selector (e.g. <c>{job="comuki-orchestrator"}</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching label-set rows.</returns>
    [Get("/api/v1/series")]
    public Task<IReadOnlyList<SeriesMatchWire>> SeriesAsync(
        [Query] string match,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Prometheus HTTP envelope shape: <c>status</c> + the <c>data</c>
/// payload. <typeparamref name="TWire"/> is the per-endpoint typed
/// payload (matrix values, instant values, etc.).
/// </summary>
internal sealed record PrometheusResponseEnvelope<TWire>(
    string Status,
    PrometheusDataWire<TWire>? Data);

/// <summary>The Prometheus <c>data</c> field: <c>resultType</c> + payload.</summary>
internal sealed record PrometheusDataWire<TWire>(
    string ResultType,
    IReadOnlyList<TWire>? Result);

/// <summary>
/// One Prometheus value-or-matrix row. <see cref="Metric"/> carries the
/// label dictionary (instant + matrix use the same shape); <see cref="Value"/>
/// is a one-element <c>[unixMs, value]</c> pair for instant queries (vector)
/// and <see cref="Values"/> is a list of such pairs for range queries
/// (matrix). Both fields are nullable on the wire — STJ drops whichever
/// the response doesn't carry; the typed client picks the present side per
/// <see cref="PrometheusDataWire{TWire}.ResultType"/>.
/// </summary>
internal sealed record PrometheusValueWire(
    PrometheusMetricLabelsWire Metric,
    IReadOnlyList<object>? Value,
    IReadOnlyList<IReadOnlyList<object>>? Values);

/// <summary>
/// The label dictionary of a Prometheus metric row. Refit parses the
/// flat JSON object into a dictionary; <see cref="Unknown"/> carries
/// the raw JSON text for fields the wire shape doesn't pre-model
/// (the Prometheus contract is open-ended).
/// </summary>
internal sealed record PrometheusMetricLabelsWire(
    IReadOnlyDictionary<string, string> Labels,
    string? Unknown = null);

/// <summary>One <c>/api/v1/series</c> row — a flat label dictionary for a matching series.</summary>
internal sealed record SeriesMatchWire(
    IReadOnlyDictionary<string, string> Labels);
