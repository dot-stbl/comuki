using ProtoBuf;

namespace Comuki.Shared.Contracts.Grpc;

/// <summary>
/// Soft-stop: the orchestrator asks the worker to abandon the current item.
/// The Translator kills the pi process tree and fails the item with the
/// given reason.
/// </summary>
[ProtoContract]
public sealed record Stop
{
    [ProtoMember(1)]
    public string Reason { get; init; } = string.Empty;
}
