using ProtoBuf;

namespace Comuki.Shared.Contracts.Grpc;

/// <summary>
/// First event of a claimed work item: the worker announces which item it
/// started, the run it belongs to, and the brief it is executing.
/// </summary>
[ProtoContract]
public sealed record StageStart
{
    [ProtoMember(1)]
    public string WorkItemId { get; init; } = string.Empty;

    [ProtoMember(2)]
    public string RunId { get; init; } = string.Empty;

    [ProtoMember(3)]
    public string Brief { get; init; } = string.Empty;
}
