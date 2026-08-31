using ProtoBuf;

namespace Comuki.Shared.Contracts.Grpc;

/// <summary>
/// Command the Orchestrator pushes to the worker over the bidi stream,
/// discriminated by which optional field is set:
/// <see cref="Stop"/> | <see cref="InjectContext"/> | <see cref="LeaseExpired"/>.
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
}
