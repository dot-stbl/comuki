using ProtoBuf;

namespace Comuki.Platform.Orchestration.Grpc;

[ProtoContract]
public sealed record StageStart
{
    [ProtoMember(1)] public string TaskId { get; init; } = string.Empty;
    [ProtoMember(2)] public string StageId { get; init; } = string.Empty;
    [ProtoMember(3)] public string Brief { get; init; } = string.Empty;
}
