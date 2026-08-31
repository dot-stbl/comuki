using Comuki.Shared.Kernel.Ids;

namespace Comuki.Engine.Orchestration.Domain.Journal;

/// <summary>
/// Append-only journal entry of a run timeline: one row per state change or
/// worker report. <see cref="Payload"/> is raw JSON (<c>jsonb</c> column) —
/// its shape is open and per-<see cref="Type"/>.
/// </summary>
public sealed class RunEvent
{
    internal RunEvent()
    {
    }

    /// <summary>Journal entry id (UUIDv7, client-side).</summary>
    public Guid Id { get; private set; }

    /// <summary>The run this entry belongs to.</summary>
    public RunId RunId { get; private set; }

    /// <summary>Event type — a stable dot.case string, see <see cref="RunEventTypes"/>.</summary>
    public string Type { get; private set; } = string.Empty;

    /// <summary>Raw JSON payload (<c>jsonb</c> column).</summary>
    public string Payload { get; private set; } = string.Empty;

    /// <summary>When the event happened (run timeline ordering key).</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>Creates a journal entry; type and payload must be non-empty.</summary>
    /// <param name="runId"></param>
    /// <param name="type"></param>
    /// <param name="payloadJson"></param>
    /// <param name="occurredAt"></param>
    /// <exception cref="ArgumentException"></exception>
    public static RunEvent Create(RunId runId, string type, string payloadJson, DateTimeOffset occurredAt)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            throw new ArgumentException("event type must not be empty", nameof(type));
        }

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            throw new ArgumentException("event payload must not be empty", nameof(payloadJson));
        }

        var id = Guid.CreateVersion7();
        return new RunEvent
        {
            Id = id,
            RunId = runId,
            Type = type,
            Payload = payloadJson,
            OccurredAt = occurredAt,
        };
    }
}
