using System.Diagnostics;
using System.Globalization;
using Comuki.Modules.Observability.Application.Ports;
using Comuki.Modules.Observability.Domain;
using Comuki.Modules.Observability.Domain.Logs;
using Microsoft.Extensions.Logging;
using Refit;

namespace Comuki.Modules.Observability.Infrastructure.VictoriaLogs;

/// <summary>
/// Refit-backed <see cref="IVictoriaLogsQueryClient"/> implementation.
/// The client constructs its own <see cref="HttpClient"/> + Refit
/// proxy lazily from the <see cref="IVictoriaEndpointResolver"/>
/// (singleton, registered at composition); the Resolver's
/// <c>ResolveLogsUrl</c> hands the base URL the first call resolves,
/// which keeps the operator-override contract (<c>LogsBaseUrl</c>)
/// in one place. The Refit surface is generated against a private
/// HttpClient — the typed client is the boundary, the resolver is
/// the source.
/// </summary>
internal sealed class VictoriaLogsQueryClient : IVictoriaLogsQueryClient
{
    private readonly IVictoriaLogsApi api;
    private readonly ILogger<VictoriaLogsQueryClient> logger;

    /// <summary>Construct the typed client: snapshot the resolver's base URL into a private HttpClient + Refit proxy.</summary>
    /// <param name="resolver">The endpoint resolver (singleton).</param>
    /// <param name="logger">The structured logger for diagnostics on transport failures.</param>
    public VictoriaLogsQueryClient(
        IVictoriaEndpointResolver resolver,
        ILogger<VictoriaLogsQueryClient> logger)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(logger);

        this.logger = logger;
        var httpClient = new HttpClient
        {
            BaseAddress = resolver.ResolveLogsUrl(),
            Timeout = TimeSpan.FromSeconds(15),
        };
        api = RestService.For<IVictoriaLogsApi>(httpClient);
    }

    /// <summary>Construction for tests: inject a pre-built Refit proxy.</summary>
    /// <param name="api">The Refit-generated proxy to call.</param>
    /// <param name="logger">The structured logger for diagnostics.</param>
    internal VictoriaLogsQueryClient(
        IVictoriaLogsApi api,
        ILogger<VictoriaLogsQueryClient> logger)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(logger);
        this.api = api;
        this.logger = logger;
    }
    /// <inheritdoc />
    public async Task<IReadOnlyList<LogRow>> SearchAsync(
        LogsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var effectiveQuery = WithAmbientTraceId(query);
        try
        {
            using var _ = ActivityScope(effectiveQuery.TraceId, log =>
                logger.LogDebug(
                    "executing LogsQL query {Query} with trace {TraceId}",
                    log.Query, log.TraceId));

            var wire = await api.QueryAsync(
                new LogsQlQueryRequest(
                    Query: effectiveQuery.Query,
                    Time: FormatTimeRange(effectiveQuery.FromUnixMs, effectiveQuery.ToUnixMs),
                    Limit: effectiveQuery.Limit),
                cancellationToken).ConfigureAwait(false);

            return MapRecords(wire);
        }
        catch (ApiException api)
        {
            throw new VictoriaUnavailableException("victoria-logs", api);
        }
        catch (HttpRequestException transport)
        {
            throw new VictoriaUnavailableException("victoria-logs", transport);
        }
        catch (TaskCanceledException timeout) when (!cancellationToken.IsCancellationRequested)
        {
            throw new VictoriaUnavailableException("victoria-logs", timeout);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LogRow>> ContextAsync(
        string traceId,
        long? fromUnixMs = null,
        long? toUnixMs = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(traceId))
        {
            throw new ArgumentException("traceId is required", nameof(traceId));
        }

        try
        {
            using var _ = ActivityScope(traceId, log =>
                logger.LogDebug("querying log context for trace {TraceId}", log.TraceId));

            var wire = await api.ContextAsync(new TraceIdRequest(traceId), cancellationToken)
                .ConfigureAwait(false);

            var rows = MapRecords(wire);

            if (fromUnixMs.HasValue || toUnixMs.HasValue)
            {
                rows = [.. rows.Where(row => row.Timestamp.ToUnixTimeMilliseconds() >= (fromUnixMs ?? long.MinValue)
                                          && row.Timestamp.ToUnixTimeMilliseconds() <= (toUnixMs ?? long.MaxValue))];
            }

            if (limit.HasValue && rows.Count > limit.Value)
            {
                rows = [.. rows.Take(limit.Value)];
            }

            return rows;
        }
        catch (ApiException api)
        {
            throw new VictoriaUnavailableException("victoria-logs", api);
        }
        catch (HttpRequestException transport)
        {
            throw new VictoriaUnavailableException("victoria-logs", transport);
        }
        catch (TaskCanceledException timeout) when (!cancellationToken.IsCancellationRequested)
        {
            throw new VictoriaUnavailableException("victoria-logs", timeout);
        }
    }

    /// <summary>
    /// Apply the trace-id filter: an explicit <see cref="LogsQuery.TraceId"/>
    /// wins; otherwise the ambient OTel activity's <see cref="Activity.TraceId"/>
    /// threads through as a default <c>trace_id</c> LogsQL clause per
    /// <c>specs/observability/spec.md</c> "Trace correlation by default".
    /// The append is idempotent — repeated <c>trace_id:</c> clauses
    /// collapse to a single filter so a caller-supplied override does
    /// not double up.
    /// </summary>
    private static LogsQuery WithAmbientTraceId(LogsQuery query)
    {
        var ambientTraceId = Activity.Current?.TraceId.ToString();
        var effective = query.TraceId ?? ambientTraceId;

        if (effective is null)
        {
            return query;
        }

        var needle = $"trace_id:{effective}";
        if (query.Query.Contains(needle, StringComparison.Ordinal))
        {
            return query with { TraceId = effective };
        }

        var scoped = $"{query.Query} {needle}";
        return query with { Query = scoped, TraceId = effective };
    }

    /// <summary>
    /// Run <paramref name="configure"/> with the structured-log record
    /// already filled in. The <see cref="ActivityScope"/> is currently
    /// a no-op placeholder — the trace id is captured in the log line
    /// directly (<c>{TraceId}</c> placeholder) rather than a child
    /// activity, because the typed client is a query proxy and does
    /// not own the caller's trace. Keeping the helper means the
    /// call-sites do not need <c>if (traceId is not null) { ... }</c>
    /// blocks: the helper accepts null and yields a no-op.
    /// </summary>
    private static IDisposable? ActivityScope(string? traceId, Action<ActivityScopeLog> configure)
    {
        if (traceId is null)
        {
            return null;
        }

        configure(new ActivityScopeLog(traceId));
        return null;
    }

    private readonly record struct ActivityScopeLog(string TraceId, string Query = "");

    /// <summary>
    /// Build the LogsQL <c>time</c> parameter from the optional unix-ms
    /// window. Format is <c>[from, to)</c> when both halves are present,
    /// one-sided otherwise, omitted entirely when both halves are null
    /// (the open-window case). The unix-ms wire-format is
    /// locale-independent — we go through
    /// <see cref="CultureInfo.InvariantCulture"/> so a de-DE worker
    /// does not emit <c>12.345</c> with a German thousands separator.
    /// </summary>
    private static string? FormatTimeRange(long? fromUnixMs, long? toUnixMs)
    {
        if (fromUnixMs is null && toUnixMs is null)
        {
            return null;
        }

        var from = fromUnixMs?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        var to = toUnixMs?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        return $"time:{from}..{to}";
    }

    /// <summary>Map the wire envelope onto the typed <see cref="LogRow"/> list.</summary>
    private static IReadOnlyList<LogRow> MapRecords(LogQueryResponseEnvelope envelope)
    {
        if (envelope.Records is null || envelope.Records.Count == 0)
        {
            return [];
        }

        var rows = new List<LogRow>(envelope.Records.Count);
        foreach (var wire in envelope.Records)
        {
            // The wire shape carries _time as a unix-ms string per the
            // VictoriaLogs contract. Parse via invariant culture (the
            // unix-ms wire-format is locale-independent).
            var timestamp = long.TryParse(wire.Time, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var unixMs)
                ? DateTimeOffset.FromUnixTimeMilliseconds(unixMs)
                : DateTimeOffset.MinValue;

            rows.Add(new LogRow(
                Timestamp: timestamp,
                Level: wire.Level,
                MessageTemplate: wire.Message,
                ScopeJson: wire.Fields ?? "{}",
                TraceId: wire.TraceId,
                SpanId: wire.SpanId));
        }

        return rows;
    }
}
