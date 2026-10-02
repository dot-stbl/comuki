using ProtoBuf;

namespace Comuki.Shared.Contracts.Grpc;

/// <summary>
/// Stage condition event (harden-pi-worker-sandbox 5.1, spec D6).
/// The Translator signals a boolean condition on the bound run:
/// <c>WorkspacePrepared</c> after the clone + restore step,
/// <c>EgressApplied</c> after the compute provider fences the worker
/// (the Translator surfaces what the claim told it; the actual fence
/// lives on the host side), and <c>AgentRunning</c> once the pi
/// process has started. Conditions are journal events, not columns —
/// the dashboard reads them off the timeline; the reaper still keys
/// off the lease.
/// </summary>
[ProtoContract]
public sealed record StageCondition
{
    /// <summary>Work item id this condition belongs to (matches the claim).</summary>
    [ProtoMember(1)]
    public string WorkItemId { get; init; } = string.Empty;

    /// <summary>Condition name — <c>WorkspacePrepared</c>, <c>EgressApplied</c>, <c>AgentRunning</c>.</summary>
    [ProtoMember(2)]
    public string Name { get; init; } = string.Empty;

    /// <summary>Boolean value of the condition (true = achieved).</summary>
    [ProtoMember(3)]
    public bool Value { get; init; }
}
