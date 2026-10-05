using Refit;

namespace Comuki.Modules.Observability.Infrastructure.VictoriaMetrics;

/// <summary>
/// VictoriaMetrics Prometheus-compatible HTTP surface — the typed
/// contract the <c>IVictoriaMetricsQueryClient</c> uses. The
/// path segments are relative to the base URL the host composition
/// binds through
/// <c>AddRefitClient&lt;IVictoriaMetricsApi&gt;().ConfigureHttpClient(...).AddStandardResilienceHandler()</c>
/// (compose's <c>victoria-metrics</c> on the port
/// <c>ObservabilityOptions.DefaultMetricsPort</c>). The wire shape
/// mirrors the Prometheus HTTP API:
/// <c>/api/v1/query</c> + <c>/api/v1/query_range</c> + <c>/api/v1/series</c>
/// — typed envelope <c>PrometheusResponseEnvelope</c> for the
/// <c>status</c> + <c>data</c> JSON envelope, the typed
/// <c>SeriesMatchWire</c> for the flat series-array shape.
/// </summary>
internal interface IVictoriaMetricsApi
{
    /// <summary>
    /// PromQL instant query. <paramref name="time"/> is unix <em>seconds</em>
    /// (Prometheus convention — <see href="https://prometheus.io/docs/prometheus/latest/querying/api/#time-series-selectors"/>),
    /// not milliseconds; the typed client converts.
    /// </summary>
    /// <param name="query">URL-encoded PromQL expression (Refit escapes it for us).</param>
    /// <param name="time">Optional unix-seconds evaluation timestamp.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The wire-shaped Prometheus response envelope.</returns>
    [Get("/api/v1/query")]
    public Task<IApiResponse<PrometheusResponseEnvelope<PrometheusValueWire>>> QueryAsync(
        [Query] string query,
        [Query] string? time = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// PromQL range query (matrix). <paramref name="start"/>,
    /// <paramref name="end"/>, <paramref name="step"/> are all unix
    /// <em>seconds</em> / a Prometheus <c>duration</c> string (e.g.
    /// <c>"30s"</c>, <c>"5m"</c>); the typed client formats
    /// <see cref="DateTimeOffset"/>s to seconds and picks a default
    /// <c>step</c> from <c>ObservabilityOptions.ScrapeInterval</c> when
    /// absent.
    /// </summary>
    /// <param name="query">URL-encoded PromQL expression.</param>
    /// <param name="start">unix-seconds inclusive lower bound.</param>
    /// <param name="end">unix-seconds inclusive upper bound.</param>
    /// <param name="step">Resolution of the returned matrix (Prometheus duration or unix seconds).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The wire-shaped Prometheus response envelope.</returns>
    [Get("/api/v1/query_range")]
    public Task<IApiResponse<PrometheusResponseEnvelope<PrometheusValueWire>>> QueryRangeAsync(
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
    public Task<IApiResponse<IReadOnlyList<SeriesMatchWire>>> SeriesAsync(
        [Query] string match,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Prometheus HTTP envelope shape: <c>status</c> + the <c>data</c>
/// payload. <typeparamref name="TWire"/> is the per-endpoint typed
/// payload (matrix values, instant values, etc.). The infrastructure
/// branch rejects on a non-<c>"success"</c> status string (the wire
/// contract documents both).
/// </summary>
internal sealed record PrometheusResponseEnvelope<TWire>(
    string Status,
    PrometheusDataWire<TWire>? Data);

/// <summary>The Prometheus <c>data</c> field: <c>resultType</c> + payload.</summary>
internal sealed record PrometheusDataWire<TWire>(
    string ResultType,
    IReadOnlyList<TWire>? Result);

/// <summary>
/// One Prometheus value-or-matrix row. <see cref="Metric"/> carries
/// the label dictionary (instant + matrix use the same shape);
/// <see cref="Value"/> is a one-element <c>[unixSec, "str"]</c> pair for
/// instant queries (vector) and <see cref="Values"/> is a list of
/// such pairs for range queries (matrix). Both fields are nullable on
/// the wire — STJ drops whichever the response doesn't carry; the
/// typed client picks the present side per
/// <see cref="PrometheusDataWire{TWire}.ResultType"/>. Timestamps on
/// the wire are unix <em>seconds</em> (Prometheus convention), not
/// milliseconds.
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
