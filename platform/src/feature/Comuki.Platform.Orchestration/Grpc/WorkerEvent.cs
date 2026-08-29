using ProtoBuf;

namespace Comuki.Platform.Orchestration.Grpc;

/// <summary>
/// Event emitted by the worker over the bidi stream. Discriminated by which
/// optional field is set: Start | Activity | Report. Corresponds to the
/// Claude Code / pi stream-json event types the Translator parses in 04-01.
/// </summary>
[ProtoContract]
public sealed record WorkerEvent
{
    [ProtoMember(1)] public StageStart? Start { get; init; }
    [ProtoMember(2)] public StageActivity? Activity { get; init; }
    [ProtoMember(3)] public StageReport? Report { get; init; }
}
