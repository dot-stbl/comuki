namespace Comuki.Modules.Observability.Domain.Metrics;

/// <summary>
/// Inputs to <c>observability.metrics.query</c>. <see cref="PromQl"/> is
/// the raw PromQL expression (VictoriaMetrics speaks the Prometheus
/// query API at <c>/api/v1/query</c> and <c>/api/v1/query_range</c>).
/// The query is either an **instant** (set <see cref="TimeUnixMs"/>,
/// leave the range fields null) or a **range** (set
/// <see cref="FromUnixMs"/> + <see cref="ToUnixMs"/> + <see cref="StepUnixMs"/>,
/// leave <see cref="TimeUnixMs"/> null); the two shapes are mutually
/// exclusive — the MCP handler validates them and the typed client
/// dispatches to <c>/api/v1/query</c> vs <c>/api/v1/query_range</c>
/// accordingly. The range resolution defaults to 15s in
/// <c>Observability:Victoria:ScrapeInterval</c>.
/// </summary>
/// <param name="PromQl">PromQL expression.</param>
/// <param name="TimeUnixMs">Instant-query evaluation time (unix ms, UTC); null for range queries.</param>
/// <param name="FromUnixMs">Range-query inclusive lower bound (unix ms, UTC); null for instant queries.</param>
/// <param name="ToUnixMs">Range-query inclusive upper bound (unix ms, UTC); null for instant queries.</param>
/// <param name="StepUnixMs">Resolution of the returned matrix; null = server default.</param>
/// <param name="TraceId">Explicit W3C trace id override; <see langword="null"/> means the client picks the caller's ambient activity's trace id.</param>
public sealed record MetricsQuery(
    string PromQl,
    long? TimeUnixMs = null,
    long? FromUnixMs = null,
    long? ToUnixMs = null,
    long? StepUnixMs = null,
    string? TraceId = null);
