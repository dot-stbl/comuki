using ProtoBuf;

namespace Comuki.Shared.Contracts.Grpc;

/// <summary>
/// Backpressure drop event (harden-worker-runtime Phase 3, design D4).
/// The harness events channel dropped a progress-fragment because the
/// consumer fell behind the producer. Mandatory events
/// (<c>StageStart</c>, <c>StageReport</c>, <c>agent_end</c>) never drop
/// — they wait. The host journals it as <c>worker.events_dropped</c>
/// with a payload (<c>{ workItemId, kind }</c>) and increments the
/// <c>events_dropped_total</c> counter so the operator can see
/// sustained backpressure on a noisy harness.
/// </summary>
[ProtoContract]
public sealed record StageEventsDropped
{
    /// <summary>Work item the drop is bound to.</summary>
    [ProtoMember(1)]
    public string WorkItemId { get; init; } = string.Empty;

    /// <summary>Drop reason. The open set today is <c>"progress"</c>; the field stays as a string so future kinds (e.g. <c>"text_delta"</c>) can extend without a wire-change.</summary>
    [ProtoMember(2)]
    public string Kind { get; init; } = "progress";
}
