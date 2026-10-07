using ProtoBuf;

namespace Comuki.Shared.Contracts.Grpc;

/// <summary>
/// Stall-detected event (harden-worker-runtime Phase 1, design D1 + D2).
/// The Translator's <c>WorkerProgressWatchdog</c> /
/// <c>DeadlinePolicy</c> escalated past the gentle-kill tier and
/// set <c>ShouldFailItem = true</c> with a typed <c>FailReason</c>
/// (<c>worker.stall_detected</c>,
/// <c>worker.turn_budget_exceeded</c>,
/// <c>worker.run_budget_exceeded</c>); the pump returns a
/// <c>PiOutcome.FailedStatus</c> with that reason and the loop's
/// existing <c>api.FailAsync</c> call (in <c>TranslatorLoop</c>)
/// carries the same reason on the wire. The host journals it as
/// <c>worker.stall_detected</c> with a payload
/// (<c>{ workItemId, last_event_age_ms, turn_elapsed_ms,
/// run_elapsed_ms, tier, reason }</c>) so the operator can
/// correlate progress-stall against wall-clock breaches. Tier 3
/// — the item is failed; the run's terminal event is independent.
/// </summary>
[ProtoContract]
public sealed record StageStallDetected
{
    /// <summary>Work item the stall is bound to.</summary>
    [ProtoMember(1)]
    public string WorkItemId { get; init; } = string.Empty;

    /// <summary>Time since the last parsed stream-event, in milliseconds.</summary>
    [ProtoMember(2)]
    public long LastEventAgeMs { get; init; }

    /// <summary>Time since the current cycle's spawn, in milliseconds.</summary>
    [ProtoMember(3)]
    public long TurnElapsedMs { get; init; }

    /// <summary>Time since the worker process started, in milliseconds.</summary>
    [ProtoMember(4)]
    public long RunElapsedMs { get; init; }

    /// <summary>Escalation tier the stall reached (3 = fail-item).</summary>
    [ProtoMember(5)]
    public int Tier { get; init; }

    /// <summary>Reason the watchdog attached to the fail-item call.</summary>
    [ProtoMember(6)]
    public string Reason { get; init; } = string.Empty;
}
