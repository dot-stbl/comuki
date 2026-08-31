using ProtoBuf;

namespace Comuki.Shared.Contracts.Grpc;

/// <summary>
/// Mid-run context push: the orchestrator appends context (e.g. a PR
/// comment or a clarification) into the worker's environment. V0 appends
/// it to a file in the working directory.
/// </summary>
[ProtoContract]
public sealed record InjectContext
{
    [ProtoMember(1)]
    public string Context { get; init; } = string.Empty;
}
