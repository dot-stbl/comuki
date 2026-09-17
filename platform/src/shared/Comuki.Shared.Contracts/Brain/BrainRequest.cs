using ProtoBuf;

namespace Comuki.Shared.Contracts.Brain;

/// <summary>
/// One brain invocation: what to do (<see cref="Kind"/> — a key from
/// <see cref="BrainRequestKindKeys"/>), the assembled context
/// (<see cref="ContextJson"/> — caller-built: digest, run state, reports)
/// and the task itself. Callers journal what they fed here; the brain
/// stays stateless across calls.
/// <para>
/// <see cref="ScopeKind"/> / <see cref="SubjectId"/> are the optional
/// scope hook for callers that know the real project/user the call
/// belongs to. When <see cref="ScopeKind"/> is null the brain falls
/// back to the legacy global-only behaviour (the caller-built digest
/// in <see cref="ContextJson"/> is what the model sees). When it is
/// set, <see cref="SubjectId"/> (Guid as string) is required and the
/// brain fetches a scope-aware digest via <c>IMemoryDigest</c> and
/// prepends it to the caller-built context before the model loop.
/// </para>
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

    /// <summary>
    /// Wire-compatible scope label (<c>"user"</c> | <c>"project"</c> |
    /// <c>"global"</c>). Null keeps the legacy global-only default so
    /// existing callers — the orchestrator auto-replan and any pre-scope
    /// gRPC client — keep working unchanged.
    /// </summary>
    [ProtoMember(4)]
    public string? ScopeKind { get; init; }

    /// <summary>
    /// Owner id inside <see cref="ScopeKind"/> (user id, project id) as
    /// its canonical Guid string. Required when <see cref="ScopeKind"/>
    /// is non-null; ignored when <see cref="ScopeKind"/> is null.
    /// </summary>
    [ProtoMember(5)]
    public string? SubjectId { get; init; }
}
