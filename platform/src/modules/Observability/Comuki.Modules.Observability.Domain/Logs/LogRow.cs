namespace Comuki.Modules.Observability.Domain.Logs;

/// <summary>
/// One log record read from VictoriaLogs. The wire shape is NDJSON
/// (one JSON object per line, <c>_time</c> as RFC3339 UTC); the
/// infrastructure project parses each line into this record. The
/// shape mirrors the wire verbatim — no derived fields, no joins —
/// because the dashboard renders the record as-is.
/// </summary>
/// <param name="Timestamp">RFC3339 UTC wall-clock when the record was emitted (the wire's <c>_time</c>).</param>
/// <param name="Level">Log level (the wire's <c>level</c>; MEL records carry the standard <c>Information</c> / <c>Warning</c> / <c>Error</c> labels).</param>
/// <param name="MessageTemplate">The log message template, with placeholders unreplaced (the wire's <c>_msg</c>).</param>
/// <param name="ScopeJson">JSON-encoded scope bag (the wire's <c>_stream</c> and any other scope fields), serialised to a single string so the dashboard can render it without parsing twice.</param>
/// <param name="TraceId">W3C trace id when the record was emitted from inside an OTel activity, <c>null</c> otherwise.</param>
/// <param name="SpanId">W3C span id when the record was emitted from inside an OTel activity, <c>null</c> otherwise.</param>
public sealed record LogRow(
    DateTimeOffset Timestamp,
    string Level,
    string MessageTemplate,
    string ScopeJson,
    string? TraceId,
    string? SpanId);
