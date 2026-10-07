using ProtoBuf;

namespace Comuki.Shared.Contracts.Grpc;

/// <summary>
/// Event the worker pushes over the bidi stream, discriminated by which
/// optional field is set: <see cref="Start"/> | <see cref="Activity"/> |
/// <see cref="Report"/> | <see cref="Verify"/> | <see cref="Condition"/> |
/// <see cref="Drain"/> | <see cref="StallWarn"/> |
/// <see cref="StallDetected"/> | <see cref="EventsDropped"/>. Produced
/// by the Translator from pi's stream-json output, the isolated verify
/// runner (isolate-verifier-runtime 1.4), the sandbox conditions
/// (harden-pi-worker-sandbox 5.1), the pre-complete artifact drain
/// (harden-pi-worker-sandbox 5.2), and the runtime hardening series
/// (harden-worker-runtime 1.1/1.2/3.1); consumed by the Host gRPC
/// service and journaled.
/// </summary>
[ProtoContract]
public sealed record WorkerEvent
{
    [ProtoMember(1)]
    public StageStart? Start { get; init; }

    [ProtoMember(2)]
    public StageActivity? Activity { get; init; }

    [ProtoMember(3)]
    public StageReport? Report { get; init; }

    /// <summary>Set when the Translator's verify-profile runner finishes a
    /// verify-profile item; the host-side journal maps it to
    /// <c>verify.completed</c> / <c>verify.failed</c> entries.</summary>
    [ProtoMember(4)]
    public StageVerify? Verify { get; init; }

    /// <summary>Set when a sandbox stage flips a boolean condition
    /// (harden-pi-worker-sandbox 5.1, spec D6); the host-side journal
    /// maps it to a <c>worker.condition</c> entry.</summary>
    [ProtoMember(5)]
    public StageCondition? Condition { get; init; }

    /// <summary>Set just before the Translator calls
    /// <c>complete</c>/<c>fail</c> to flush artifacts accumulated
    /// during the run (harden-pi-worker-sandbox 5.2, spec D7); the
    /// host-side packager picks up already-bundled prefixes and
    /// skips them.</summary>
    [ProtoMember(6)]
    public StageDrain? Drain { get; init; }

    /// <summary>Set when the Translator's <c>WorkerProgressWatchdog</c>
    /// detects <c>last_event_age &gt; WorkerProgressTimeout</c> (tier 1
    /// of the escalation chain, harden-worker-runtime Phase 1, design
    /// D1). The host-side journal maps it to a
    /// <c>worker.stall_warn</c> entry.</summary>
    [ProtoMember(7)]
    public StageStallWarn? StallWarn { get; init; }

    /// <summary>Set when the Translator escalated past gentle-kill and
    /// called <c>api.FailAsync</c> with reason
    /// <c>worker.stall_detected</c> (tier 3, harden-worker-runtime
    /// Phase 1, design D1 + D2). The host-side journal maps it to a
    /// <c>worker.stall_detected</c> entry.</summary>
    [ProtoMember(8)]
    public StageStallDetected? StallDetected { get; init; }

    /// <summary>Set when the harness events channel drops a
    /// progress-fragment because the consumer fell behind
    /// (harden-worker-runtime Phase 3, design D4). The host-side
    /// journal maps it to a <c>worker.events_dropped</c> entry and
    /// increments the <c>events_dropped_total</c> counter.</summary>
    [ProtoMember(9)]
    public StageEventsDropped? EventsDropped { get; init; }
}
