namespace Comuki.Modules.Observability.Domain.Metrics;

/// <summary>
/// Inputs to <c>observability.metrics.query</c>. The shape is the typed
/// projection the MCP handlers feed onto the infrastructure client; the
/// wire-format conversion (Prometheus expects unix <em>seconds</em>, not
/// milliseconds — see <see href="https://prometheus.io/docs/prometheus/latest/querying/api/#time-series-selectors"/>
/// for the canonical timestamps reference) lives in the infrastructure
/// module. <see cref="Time"/>/<see cref="Start"/>/<see cref="End"/> are
/// UTC <see cref="DateTimeOffset"/> values; the MCP layer parses them out
/// of ISO 8601 arguments at the validation clamp. The query is either
/// an <strong>instant</strong> (set <see cref="Time"/>, leave the range
/// fields null) or a <strong>range</strong> (set <see cref="Start"/> +
/// <see cref="End"/> + <see cref="Step"/>, leave <see cref="Time"/> null);
/// the two shapes are mutually exclusive — the MCP handler validates them
/// and the typed client dispatches to <c>/api/v1/query</c> vs
/// <c>/api/v1/query_range</c> accordingly. The range step defaults to
/// <c>ObservabilityOptions.ScrapeInterval</c> when null.
/// </summary>
/// <param name="PromQl">PromQL expression.</param>
/// <param name="Time">Instant-query evaluation time (UTC wall clock); null for range queries.</param>
/// <param name="Start">Range-query inclusive lower bound (UTC wall clock); null for instant queries.</param>
/// <param name="End">Range-query inclusive upper bound (UTC wall clock); null for instant queries.</param>
/// <param name="Step">Resolution of the returned matrix; null = server default.</param>
/// <param name="TraceId">Explicit W3C trace id override; <see langword="null"/> means the client picks the caller's ambient activity's trace id.</param>
public sealed record MetricsQuery(
    string PromQl,
    DateTimeOffset? Time = null,
    DateTimeOffset? Start = null,
    DateTimeOffset? End = null,
    TimeSpan? Step = null,
    string? TraceId = null);
