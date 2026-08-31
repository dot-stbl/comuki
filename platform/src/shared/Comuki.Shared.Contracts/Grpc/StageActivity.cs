using ProtoBuf;

namespace Comuki.Shared.Contracts.Grpc;

/// <summary>
/// Live progress from the running agent, discriminated by which optional
/// field is set: a text chunk, or a tool invocation.
/// </summary>
[ProtoContract]
public sealed record StageActivity
{
    [ProtoMember(1)]
    public string WorkItemId { get; init; } = string.Empty;

    /// <summary>Set when the activity is an assistant text chunk.</summary>
    [ProtoMember(2)]
    public string? Text { get; init; }

    /// <summary>Set when the activity is a tool invocation. Name of the tool (Bash, Read, …).</summary>
    [ProtoMember(3)]
    public string? Tool { get; init; }

    /// <summary>Raw JSON of the tool input, for tools whose input shape we don't model.</summary>
    [ProtoMember(4)]
    public string? ToolInputJson { get; init; }
}
