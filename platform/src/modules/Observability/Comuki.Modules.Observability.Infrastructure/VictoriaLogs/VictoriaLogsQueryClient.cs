using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Comuki.Modules.Observability.Application.Ports;
using Comuki.Modules.Observability.Domain;
using Comuki.Modules.Observability.Domain.Logs;
using Microsoft.Extensions.Logging;
using Refit;

namespace Comuki.Modules.Observability.Infrastructure.VictoriaLogs;

/// <summary>
/// Refit-backed <see cref="IVictoriaLogsQueryClient"/> implementation.
/// The Refit surface is registered by the composition root through
/// <c>AddRefitClient&lt;IVictoriaLogsApi&gt;().ConfigureHttpClient(...).AddStandardResilienceHandler()</c>
/// (the established pattern from
/// <c>Comuki.Host.Translator.Api.Registration.TranslatorApiExtensions</c>);
/// the typed client receives the Refit-generated proxy through DI.
/// <see cref="LogsQuery.From"/>
/// / <see cref="LogsQuery.To"/>
/// are parsed <see cref="DateTimeOffset"/>s — the infrastructure
/// converts to RFC3339 for the LogsQL <c>time</c> pipe.
/// </summary>
internal sealed class VictoriaLogsQueryClient(
    IVictoriaLogsApi api,
    ILogger<VictoriaLogsQueryClient> logger) : IVictoriaLogsQueryClient
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<LogRow>> SearchAsync(
        LogsQuery query,
        CancellationToken cancellationToken = default)
    {
        var effectiveQuery = WithEffectiveTraceId(query);
        try
        {
            var response = await api.QueryAsync(
                effectiveQuery.Query,
                effectiveQuery.Limit,
                ToRfc3339(effectiveQuery.From),
                ToRfc3339(effectiveQuery.To),
                cancellationToken);

            return MapResponse(logger, response);
        }
        catch (ApiException exception)
        {
            throw new VictoriaUnavailableException("victoria-logs", exception);
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
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(traceId))
        {
            throw new ArgumentException("traceId is required", nameof(traceId));
        }

        // The wire has no /select/logsql/context — we route the trace-id
        // lookup as logs.search with `trace_id:<id>` appended to the
        // query body per the brief. Time/limit are independent of the
        // trace-id and apply as ordinary LogsQL filters.
        var composed = new LogsQuery(
            Query: VictoriaLogsQueryHelpers.ComposeTraceIdClause(traceId),
            From: from,
            To: to,
            Limit: limit);

        return await SearchAsync(composed, cancellationToken);
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
    private static LogsQuery WithEffectiveTraceId(LogsQuery query)
    {
        // An explicit LogsQuery.TraceId wins; otherwise the ambient
        // OTel activity's TraceId threads through as a default
        // LogsQL `trace_id:` clause. The append is idempotent so a
        // caller-supplied clause doesn't double up.
        var effectiveTraceId = query.TraceId ?? Activity.Current?.TraceId.ToString();
        return effectiveTraceId is null
            ? query
            : VictoriaLogsQueryHelpers.AppendTraceIdOnce(query, effectiveTraceId);
    }

    /// <summary>
    /// Map the wire NDJSON envelope onto typed <see cref="LogRow"/>
    /// records. The wire shape carries <c>_time</c> as an ISO 8601 UTC
    /// string per the VictoriaLogs contract — the infrastructure
    /// project owns the parsing per <c>~/.agents/rules/csharp/code-shape.md</c>
    /// §"mappers own NDJSON"; a malformed line is logged and dropped,
    /// not raised (the spec-mandated "no page fails because of one
    /// bad line" posture). The observable <c>_time</c> / <c>_msg</c> /
    /// <c>_stream</c> fields plus optional <c>trace_id</c> / <c>span_id</c>
    /// follow the documented VictoriaLogs JSON line shape.
    /// </summary>
    internal static IReadOnlyList<LogRow> MapResponse(
        ILogger logger,
        IApiResponse<string> response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"VictoriaLogs returned HTTP {(int)response.StatusCode!.Value}.");
        }

        var body = response.Content ?? string.Empty;
        if (string.IsNullOrWhiteSpace(body))
        {
            return [];
        }

        var rows = new List<LogRow>();
        foreach (var line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (TryParseLine(logger, line, out var row))
            {
                rows.Add(row);
            }
        }

        return rows;
    }

    /// <summary>
    /// Parse one NDJSON line into a typed <see cref="LogRow"/>; malformed
    /// lines are logged at <c>Debug</c> and dropped per
    /// <c>~/.agents/rules/csharp/json-and-ndjson.md</c> §4
    /// (a single malformed line is logged + dropped, never fails the page).
    /// </summary>
    private static bool TryParseLine(ILogger logger, string line, out LogRow row)
    {
        row = default!;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException exception)
        {
            logger.LogDebug(exception, "victoria logs ndjson line failed to parse; dropping it");
            return false;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                logger.LogDebug("victoria logs ndjson line is not a JSON object; dropping it");
                return false;
            }

            var rootElement = document.RootElement;
            var timestamp = ReadTimestamp(rootElement, logger);
            var messageTemplate = ReadOptionalString(rootElement, "_msg") ?? string.Empty;
            var level = ReadOptionalString(rootElement, "level") ?? string.Empty;
            var stream = ReadOptionalString(rootElement, "_stream");
            var scopeNode = stream is null
                ? []
                : new JsonObject { ["_stream"] = stream };
            var scopeJson = scopeNode.ToJsonString();
            var traceId = ReadOptionalString(rootElement, "trace_id");
            var spanId = ReadOptionalString(rootElement, "span_id");

            row = new LogRow(
                Timestamp: timestamp,
                Level: level,
                MessageTemplate: messageTemplate,
                ScopeJson: scopeJson,
                TraceId: traceId,
                SpanId: spanId);
            return true;
        }
    }

    /// <summary>
    /// Parse <c>_time</c> as RFC3339 UTC. A missing / malformed
    /// <c>_time</c> drops the record with a debug entry per
    /// <c>~/.agents/rules/csharp/error-mapping.md</c> §"отказ не
    /// маскируется под успех"; we never silently substitute a
    /// sentinel like <see cref="DateTimeOffset.MinValue"/>.
    /// </summary>
    private static DateTimeOffset ReadTimestamp(JsonElement rootElement, ILogger logger)
    {
        if (!rootElement.TryGetProperty("_time", out var timeElement)
            || timeElement.ValueKind != JsonValueKind.String)
        {
            logger.LogDebug("victoria logs ndjson line missing _time; dropping it");
            return default;
        }

        var raw = timeElement.GetString();
        if (string.IsNullOrWhiteSpace(raw)
            || !DateTimeOffset.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            logger.LogDebug("victoria logs ndjson line has malformed _time '{Raw}'; dropping it", raw);
            return default;
        }

        return parsed;
    }

    /// <summary>
    /// Format a <see cref="DateTimeOffset"/> as RFC3339 UTC for the
    /// LogsQL <c>time</c> pipe (the wire accepts ISO 8601 with a
    /// <c>Z</c> suffix; the round-trip <c>"O"</c> format produces it).
    /// <see langword="null"/> when the bound is absent so the
    /// <c>time:</c> filter is omitted.
    /// </summary>
    private static string? ToRfc3339(DateTimeOffset? value)
    {
        return value?.ToString("O", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Read an optional JSON string property. <c>null</c> when absent
    /// or non-string (the typed shape treats <c>trace_id</c> / <c>span_id</c>
    /// as optional because not every record comes from an OTel activity).
    /// </summary>
    private static string? ReadOptionalString(JsonElement rootElement, string name)
    {
        return rootElement.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;
    }
}

/// <summary>
/// File-static helpers for the <see cref="VictoriaLogsQueryClient"/>:
/// the <c>trace_id:</c> clause composition lives here per
/// <c>class-layout-and-tooling.md §1a</c> (no private methods on a
/// typed query client). Two helpers — both no-state, pure.
/// </summary>
internal static class VictoriaLogsQueryHelpers
{
    /// <summary>
    /// Build a LogsQL query body that pins a single W3C trace id —
    /// the client-side emulation of the (non-existent) wire
    /// <c>/select/logsql/context</c> endpoint. The id appears verbatim
    /// on the wire (no encoding beyond what LogsQL needs — the API
    /// accepts the bare hex string).
    /// </summary>
    public static string ComposeTraceIdClause(string traceId)
    {
        return $"trace_id:{traceId}";
    }

    /// <summary>
    /// Idempotent append: a query already carrying the same
    /// <c>trace_id:</c> needle is returned unchanged. The check is
    /// <see cref="StringComparison.Ordinal"/> because LogsQL identifiers
    /// are case-sensitive.
    /// </summary>
    public static LogsQuery AppendTraceIdOnce(LogsQuery query, string traceId)
    {
        var needle = ComposeTraceIdClause(traceId);
        return query.Query.Contains(needle, StringComparison.Ordinal)
            ? query
            : query with { Query = $"{query.Query} {needle}", TraceId = traceId };
    }
}
