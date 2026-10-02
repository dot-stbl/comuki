using ProtoBuf;

namespace Comuki.Shared.Contracts.Grpc;

/// <summary>
/// Command the Orchestrator pushes to the worker over the bidi stream,
/// discriminated by which optional field is set:
/// <see cref="Stop"/> | <see cref="InjectContext"/> | <see cref="LeaseExpired"/>
/// | <see cref="Exec"/>. Exactly one is meaningful per command; the wire
/// shape allows more than one to be set (legacy tolerated) and the worker
/// handler dispatches by first non-null field, stop first.
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
}
