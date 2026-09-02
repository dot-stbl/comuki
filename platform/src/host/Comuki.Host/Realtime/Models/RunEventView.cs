namespace Comuki.Host.Realtime.Models;

/// <summary>
/// Slim journal event broadcast to the <c>run:{id}</c> group after every
/// <c>run_events</c> append. The payload rides along only when it is small;
/// oversized payloads (embedded worker results) are omitted — heavy content
/// stays journal-only and the client refetches the timeline over REST, per
/// the scope-draft rule "тяжёлые payload — uri, не в hub".
/// </summary>
/// <param name="RunId">The run whose timeline this entry belongs to.</param>
/// <param name="Type">Stable dot.case journal event type.</param>
/// <param name="WorkItemId">The work item the entry is about, when the payload carries one.</param>
/// <param name="OccurredAtUnixMs">When the entry happened — UTC unix milliseconds.</param>
/// <param name="PayloadJson">Raw payload JSON, or null when omitted by the size cap.</param>
/// <param name="PayloadOmitted">Whether the payload was dropped by the size cap.</param>
public sealed record RunEventView(
    Guid RunId,
    string Type,
    Guid? WorkItemId,
    long OccurredAtUnixMs,
    string? PayloadJson,
    bool PayloadOmitted);
