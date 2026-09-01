using ProtoBuf;

namespace Comuki.Shared.Contracts.Brain;

/// <summary>
/// One brain invocation: what to do (<see cref="Kind"/> — a key from
/// <see cref="BrainRequestKindKeys"/>), the assembled context
/// (<see cref="ContextJson"/> — caller-built: digest, run state, reports)
/// and the task itself. Callers journal what they fed here; the brain
/// stays stateless across calls.
/// </summary>
[ProtoContract]
public sealed record BrainRequest
{
    [ProtoMember(1)]
    public string Kind { get; init; } = string.Empty;

    [ProtoMember(2)]
    public string ContextJson { get; init; } = string.Empty;

    [ProtoMember(3)]
    public string Task { get; init; } = string.Empty;
}
