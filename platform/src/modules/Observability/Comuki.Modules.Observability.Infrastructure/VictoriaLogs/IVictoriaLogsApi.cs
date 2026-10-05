using Refit;

namespace Comuki.Modules.Observability.Infrastructure.VictoriaLogs;

/// <summary>
/// VictoriaLogs LogsQL HTTP surface — the typed contract the
/// <c>IVictoriaLogsQueryClient</c> uses. The path is relative to
/// the base URL the host composition binds through
/// <c>AddRefitClient&lt;IVictoriaLogsApi&gt;</c> (compose's
/// <c>victoria-logs</c> on the port <c>ObservabilityOptions.DefaultLogsPort</c>).
/// The wire shape mirrors the public HTTP API at
/// <c>/select/logsql/query</c> (no <c>/select/logsql/context</c> endpoint
/// exists in the API — trace-context lookup is a <c>logs.search</c> with
/// the <c>trace_id</c> filter, per
/// <c>~/.agents/rules/csharp/anti-patterns.md</c> §"Use the registry over
/// the switch-sprawl"); the typed client returns the matched log rows
/// as a raw NDJSON body the infrastructure mapper splits into typed
/// <c>LogRow</c> records.
/// </summary>
internal interface IVictoriaLogsApi
{
    /// <summary>
    /// LogsQL search. Sends both query-arg and POST form-body variants
    /// (the wire accepts either; Refit picks the GET form here because
    /// <see cref="Domain.Logs.LogsQuery.Query"/>
    /// is short-form). The response is the raw NDJSON body — the
    /// infrastructure client splits it per
    /// <c>~/.agents/rules/csharp/json-and-ndjson.md</c> §4 (a malformed
    /// line is logged + dropped, never fails the page).
    /// </summary>
    /// <param name="query">LogsQL query body.</param>
    /// <param name="limit">Optional cap on returned rows.</param>
    /// <param name="start">Optional RFC3339 inclusive lower bound on <c>_time</c>.</param>
    /// <param name="end">Optional RFC3339 exclusive upper bound on <c>_time</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The raw NDJSON response body; <c>IApiResponse</c> carries the status code so a non-2xx surfaces as <see cref="ApiException"/>.</returns>
    [Get("/select/logsql/query")]
    public Task<IApiResponse<string>> QueryAsync(
        [Query] string query,
        [Query] int? limit = null,
        [Query] string? start = null,
        [Query] string? end = null,
        CancellationToken cancellationToken = default);
}
