using ProtoBuf;

namespace Comuki.Shared.Contracts.Grpc;

/// <summary>
/// Stall-warning event (harden-worker-runtime Phase 1, design D1). The
/// Translator's <c>WorkerProgressWatchdog</c> fires this when
/// <c>last_event_age &gt; WorkerProgressTimeout</c> for the first time
/// in a cycle. The host journals it as
/// <c>worker.stall_warn</c> with a structured payload
/// (<c>{ workItemId, last_event_age_ms, tier }</c>) so the dashboard
/// can render the stall surface as a journal column, and the
/// operator can correlate the warning with the surrounding timeline.
/// Tier 1 — no action on the harness; the lease is still held.
/// </summary>
[ProtoContract]
public sealed record StageStallWarn
{
    /// <summary>Work item the stall is bound to.</summary>
    [ProtoMember(1)]
    public string WorkItemId { get; init; } = string.Empty;

    /// <summary>Time since the last parsed stream-event, in milliseconds.</summary>
    [ProtoMember(2)]
    public long LastEventAgeMs { get; init; }

    /// <summary>Escalation tier the warning is on (1 = warn, 2 = gentle-kill, 3 = fail-item).</summary>
    [ProtoMember(3)]
    public int Tier { get; init; }
}
