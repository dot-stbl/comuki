using Comuki.Modules.Observability.Domain.Logs;

namespace Comuki.Modules.Observability.Application.Ports;

/// <summary>
/// Read-only VictoriaLogs HTTP surface. The interface methods return
/// typed DTOs (<see cref="LogsQuery"/> → <see cref="LogRow"/>), not raw
/// <c>JsonElement</c>; the wire-shape mapping is the infrastructure
/// project's job, not the application layer's. The concrete
/// implementation behind this port is a Refit client over the
/// VictoriaLogs <c>/select/logsql/*</c> endpoint group.
/// </summary>
public interface IVictoriaLogsQueryClient
{
    /// <summary>
    /// LogsQL search. The MCP tool <c>observability.logs.search</c> projects
    /// its <c>{query, from, to, limit}</c> JSON body onto this method.
    /// </summary>
    /// <param name="query">LogsQL query + optional time window / row cap.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matched log rows in time-descending order.</returns>
    /// <exception cref="Domain.VictoriaUnavailableException">
    /// The configured VictoriaLogs endpoint is unreachable for the full timeout.
    /// </exception>
    public Task<IReadOnlyList<LogRow>> SearchAsync(LogsQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Trace-context lookup. The MCP tool <c>observability.logs.context</c>
    /// projects its <c>{traceId}</c> JSON body onto this method. The
    /// wire has no <c>/select/logsql/context</c> endpoint — the typed
    /// client routes the lookup as <see cref="SearchAsync"/> with a
    /// <c>trace_id:{id}</c> clause appended to the query body
    /// (client-side emulation per the brief).
    /// </summary>
    /// <param name="traceId">W3C trace id (32 hex chars).</param>
    /// <param name="from">Optional inclusive lower bound on <c>_time</c> (UTC wall clock).</param>
    /// <param name="to">Optional exclusive upper bound on <c>_time</c> (UTC wall clock).</param>
    /// <param name="limit">Optional cap on returned rows.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Log rows emitted under the trace.</returns>
    /// <exception cref="Domain.VictoriaUnavailableException">
    /// The configured VictoriaLogs endpoint is unreachable for the full timeout.
    /// </exception>
    public Task<IReadOnlyList<LogRow>> ContextAsync(
        string traceId,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int? limit = null,
        CancellationToken cancellationToken = default);
}
