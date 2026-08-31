using ProtoBuf;

namespace Comuki.Shared.Contracts.Grpc;

/// <summary>
/// Event the worker pushes over the bidi stream, discriminated by which
/// optional field is set: <see cref="Start"/> | <see cref="Activity"/> |
/// <see cref="Report"/>. Produced by the Translator from pi's stream-json
/// output; consumed by the Host gRPC service and journaled.
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
}
