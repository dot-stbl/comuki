namespace Comuki.Modules.Observability.Domain.Logs;

/// <summary>
/// LogsQL query carried by the <c>observability.logs.search</c> MCP tool
/// and by the typed <c>IVictoriaLogsQueryClient</c> port. The query
/// body is the raw LogsQL string; the time bounds and limit are
/// optional constraints layered on top.
/// <para>
/// <see cref="TraceId"/> is the explicit-override knob for the
/// trace-correlation append (the <c>specs/observability/spec.md</c>
/// "Trace correlation by default" requirement): an explicit value
/// wins; the ambient OTel activity's trace id threads through as a
/// default <c>trace_id:</c> LogsQL clause when <see cref="TraceId"/>
/// is <c>null</c>.
/// </para>
/// </summary>
/// <param name="Query">LogsQL query body (the wire's <c>query</c>).</param>
/// <param name="From">Optional inclusive lower bound on <c>_time</c> (UTC wall clock).</param>
/// <param name="To">Optional exclusive upper bound on <c>_time</c> (UTC wall clock).</param>
/// <param name="Limit">Optional cap on returned rows.</param>
/// <param name="TraceId">Optional explicit trace id; the typed
/// client appends a <c>trace_id:</c> clause when this is set.</param>
public sealed record LogsQuery(
    string Query,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int? Limit = null,
    string? TraceId = null);
