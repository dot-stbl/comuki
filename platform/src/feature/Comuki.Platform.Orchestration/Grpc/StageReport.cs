using ProtoBuf;

namespace Comuki.Platform.Orchestration.Grpc;

[ProtoContract]
public sealed record StageReport
{
    [ProtoMember(1)] public string TaskId { get; init; } = string.Empty;
    [ProtoMember(2)] public string StageId { get; init; } = string.Empty;

    /// <summary>"success" | "failed" | "cancelled".</summary>
    [ProtoMember(3)] public string Status { get; init; } = string.Empty;

    [ProtoMember(4)] public long DurationMs { get; init; }

    [ProtoMember(5)] public string ResultText { get; init; } = string.Empty;

    [ProtoMember(6)] public string ErrorText { get; init; } = string.Empty;
}
