using Comuki.Modules.Observability.Domain.Logs;
using Refit;

namespace Comuki.Modules.Observability.Infrastructure.VictoriaLogs;

/// <summary>
/// VictoriaLogs LogsQL/select HTTP surface. The path segments are
/// relative to the base URL the
/// <see cref="Application.Ports.IVictoriaEndpointResolver"/>
/// hands the Refit client factory (compose's <c>victoria-logs</c> on
/// <see cref="Endpoint.VictoriaPorts.LogsContainerPort"/>). The shape
/// mirrors the public HTTP API at <c>/select/logsql/query</c> /
/// <c>/select/logsql/context</c>; VictoriaLogs returns the matched log
/// rows as an envelope-shaped JSON (<see cref="LogQueryResponseEnvelope"/>)
/// the client maps onto typed <see cref="LogRow"/> records.
/// </summary>
internal interface IVictoriaLogsApi
{
    /// <summary>LogsQL search.</summary>
    /// <param name="request">The query body (see <c>LogsQlQueryRequest</c> on the wire).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The wire-shaped response envelope the client maps onto <see cref="LogRow"/> records.</returns>
    [Post("/select/logsql/query")]
    public Task<LogQueryResponseEnvelope> QueryAsync(
        [Body] LogsQlQueryRequest request,
        CancellationToken cancellationToken);

    /// <summary>Trace-context lookup.</summary>
    /// <param name="traceId">W3C trace id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The wire-shaped response envelope.</returns>
    [Post("/select/logsql/context")]
    public Task<LogQueryResponseEnvelope> ContextAsync(
        [Body(BodySerializationMethod.Serialized)] TraceIdRequest traceId,
        CancellationToken cancellationToken);
}

/// <summary>
/// POST body for <c>/select/logsql/query</c>. The endpoint accepts a
/// form-urlencoded body in addition to JSON; the Refit <c>[Body]</c>
/// annotation serialises this as JSON because <c>LogsQlQueryRequest</c>
/// is a <c>JsonContent</c>-compatible record. <see cref="Query"/> is the
/// LogsQL body; <see cref="Time"/> and <see cref="Limit"/> are the
/// optional unix-ms window and row cap.
/// </summary>
internal sealed record LogsQlQueryRequest(
    string Query,
    string? Time = null,
    int? Limit = null);

/// <summary>POST body for <c>/select/logsql/context</c>.</summary>
internal sealed record TraceIdRequest(string TraceId);

/// <summary>
/// Envelope for the LogsQL/select JSON response. The wire shape uses
/// <see cref="LogRecordWire"/> records; the client maps each to a
/// typed <see cref="LogRow"/>. The fields are nullable because some
/// rows omit <c>trace_id</c> / <c>span_id</c> (logs without an OTel
/// activity) and <c>_time</c> falls back to ISO-8601 wall-clock when
/// the request did not scope by time.
/// </summary>
internal sealed record LogQueryResponseEnvelope(
    string Status,
    IReadOnlyList<LogRecordWire>? Records = null);

/// <summary>
/// One wire-shape log record. The shape follows what VictoriaLogs
/// emits at <c>/select/logsql/query</c>: a UTC unix-ms
/// <see cref="Time"/>, a <see cref="Level"/> label, an OTel-friendly
/// <see cref="Message"/> body, the JSON-encoded <see cref="Fields"/>
/// blob, and optional W3C trace/span ids. Field names match the
/// VictoriaLogs HTTP API verbatim.
/// </summary>
internal sealed record LogRecordWire(
    string Time,
    string Level,
    string Message,
    string? Fields = null,
    string? TraceId = null,
    string? SpanId = null);
