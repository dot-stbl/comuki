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
    /// Trace-context lookup: matching log rows + matching trace spans.
    /// The MCP tool <c>observability.logs.context</c> projects its
    /// <c>{traceId}</c> JSON body onto this method (the trace-span side
    /// is currently a thin pass-through that returns rows only — the
    /// span-shaped return lives in the infrastructure module when the
    /// Critic-sweep ships the matching trace store).
    /// </summary>
    /// <param name="traceId">W3C trace id (32 hex chars).</param>
    /// <param name="fromUnixMs">Optional inclusive lower bound on _time, unix ms.</param>
    /// <param name="toUnixMs">Optional inclusive upper bound on _time, unix ms.</param>
    /// <param name="limit">Optional cap on returned rows.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Log rows emitted under the trace.</returns>
    /// <exception cref="Domain.VictoriaUnavailableException">
    /// The configured VictoriaLogs endpoint is unreachable for the full timeout.
    /// </exception>
    public Task<IReadOnlyList<LogRow>> ContextAsync(
        string traceId,
        long? fromUnixMs = null,
        long? toUnixMs = null,
        int? limit = null,
        CancellationToken cancellationToken = default);
}
