using ProtoBuf;

namespace Comuki.Shared.Contracts.Brain;

/// <summary>
/// One streamed brain update. Progress chunks carry <see cref="Text"/>
/// (tool usage, iteration notes); the LAST chunk has <see cref="IsFinal"/>
/// set and <see cref="FinalJson"/> holding the result — the canonical
/// plan JSON for <c>plan</c> requests, the answer text for the rest.
/// </summary>
[ProtoContract]
public sealed record BrainChunk
{
    [ProtoMember(1)]
    public int Seq { get; init; }

    [ProtoMember(2)]
    public string Text { get; init; } = string.Empty;

    [ProtoMember(3)]
    public string FinalJson { get; init; } = string.Empty;

    [ProtoMember(4)]
    public bool IsFinal { get; init; }
}
