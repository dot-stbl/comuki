using ProtoBuf;

namespace Comuki.Shared.Contracts.Grpc;

/// <summary>
/// Final event of a work item execution: bottom-line status, wall-clock
/// duration and the result/error text. Always the last WorkerEvent of a
/// claimed item.
/// </summary>
[ProtoContract]
public sealed record StageReport
{
    [ProtoMember(1)]
    public string WorkItemId { get; init; } = string.Empty;

    /// <summary><c>success</c> | <c>failed</c> | <c>cancelled</c>.</summary>
    [ProtoMember(2)]
    public string Status { get; init; } = string.Empty;

    [ProtoMember(3)]
    public long DurationMs { get; init; }

    [ProtoMember(4)]
    public string ResultText { get; init; } = string.Empty;

    [ProtoMember(5)]
    public string ErrorText { get; init; } = string.Empty;
}
