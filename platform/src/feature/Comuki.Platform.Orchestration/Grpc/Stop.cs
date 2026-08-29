using ProtoBuf;

namespace Comuki.Platform.Orchestration.Grpc;

[ProtoContract]
public sealed record Stop
{
    [ProtoMember(1)] public string Reason { get; init; } = string.Empty;
}
