using ProtoBuf;

namespace Comuki.Platform.Orchestration.Grpc;

/// <summary>
/// Command sent by the Orchestrator to the worker over the bidi stream.
/// Discriminated by which optional field is set: Stop for now (Cancel/Retry
/// land later as the protocol grows).
/// </summary>
[ProtoContract]
public sealed record OrchestratorCommand
{
    [ProtoMember(1)] public Stop? Stop { get; init; }
}
