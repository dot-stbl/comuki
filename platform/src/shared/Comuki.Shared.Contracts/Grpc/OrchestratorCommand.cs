using ProtoBuf;

namespace Comuki.Shared.Contracts.Grpc;

/// <summary>
/// Command the Orchestrator pushes to the worker over the bidi stream,
/// discriminated by which optional field is set:
/// <see cref="Stop"/> | <see cref="InjectContext"/> | <see cref="LeaseExpired"/>
/// | <see cref="Exec"/> | <see cref="TurnInput"/>. Exactly one is meaningful
/// per command; the wire shape allows more than one to be set (legacy
/// tolerated) and the worker handler dispatches by first non-null field,
/// stop first.
/// </summary>
[ProtoContract]
public sealed record OrchestratorCommand
{
    [ProtoMember(1)]
    public Stop? Stop { get; init; }

    [ProtoMember(2)]
    public InjectContext? InjectContext { get; init; }

    [ProtoMember(3)]
    public LeaseExpired? LeaseExpired { get; init; }

    /// <summary>
    /// Opt-in operator exec (spec "Operator debug is opt-in"). Default-off
    /// on the worker; the worker handler refuses it when its
    /// <c>Translator:DebugExec</c> flag is false. Worker side, never
    /// reaches the pi (agent) process.
    /// </summary>
    [ProtoMember(4)]
    public Exec? Exec { get; init; }

    /// <summary>
    /// Live-session turn input (add-orchestra Phase 1c,
    /// <c>specs/session/spec.md</c> Requirement "TurnInput is the
    /// authoritative session turn"). Carried as one
    /// <see cref="TurnInput"/> record — the worker forwards it as a
    /// session turn on the live agent process when the harness
    /// declares <c>Capabilities.LiveSession = true</c>; otherwise the
    /// platform falls back to a follow-up research <c>WorkItem</c>.
    /// </summary>
    [ProtoMember(5)]
    public TurnInput? TurnInput { get; init; }
}
