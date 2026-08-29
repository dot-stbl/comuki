using ProtoBuf;

namespace Comuki.Platform.Orchestration.Grpc;

[ProtoContract]
public sealed record StageActivity
{
    [ProtoMember(1)] public string TaskId { get; init; } = string.Empty;
    [ProtoMember(2)] public string StageId { get; init; } = string.Empty;

    /// <summary>Set when the activity is an assistant text chunk.</summary>
    [ProtoMember(3)] public string? Text { get; init; }

    /// <summary>Set when the activity is a tool invocation. Name of the tool (Bash, Read, …).</summary>
    [ProtoMember(4)] public string? Tool { get; init; }

    /// <summary>Raw JSON of the tool input, for tools whose input shape we don't model.</summary>
    [ProtoMember(5)] public string? ToolInputJson { get; init; }
}
