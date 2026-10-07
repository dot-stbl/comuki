using ProtoBuf;

namespace Comuki.Shared.Contracts.Grpc;

/// <summary>
/// Live-session turn input. Carried as a variant on
/// <see cref="OrchestratorCommand.TurnInput"/> and forwarded by the
/// Translator as a *session turn* on the live agent process (the
/// <c>add-orchestra</c> Phase 1c spec, <c>specs/session/spec.md</c>
/// Requirement "TurnInput is the authoritative session turn"). Authoritative
/// exactly when the active execution's harness declares
/// <c>Capabilities.LiveSession = true</c>; otherwise the worker falls
/// back to a follow-up research <c>WorkItem</c> per the cowork 11.1
/// fallback path.
/// </summary>
/// <remarks>
/// Wire-format rules:
/// <list type="bullet">
///   <item>The <see cref="Text"/> body is what the agent sees as the
///   new user turn on its session — operator's steer text, chart-formatted
///   patch suggestion, or chat reply. The worker does not parse
///   <see cref="Text"/> before handing it to the harness; the harness
///   receives it verbatim.</item>
///   <item><see cref="Role"/> is stable for the 1c surface (the
///   "user" role is the only role the agent stream accepts in 1c; the
///   field exists so the v2 cowork-11.1 extension can carry
///   "system" / "tool" without a wire change).</item>
///   <item><see cref="Metadata"/> is a free-form transport for
///   harness-specific hints (e.g. <c>as: steer</c> vs <c>as:
///   follow_up</c>); the worker treats it as opaque and threads it to
///   the harness's session transport. <see cref="Dictionary{TKey,TValue}"/>
///   (not <see cref="IReadOnlyDictionary{TKey,TValue}"/>) — the concrete
///   type is what protobuf-net serializes; the read-only interface
///   wouldn't survive the protobuf-net roundtrip.</item>
/// </list>
/// </remarks>
[ProtoContract]
public sealed record TurnInput
{
    /// <summary>
    /// The turn body. Mandatory: a TurnInput with empty <see cref="Text"/>
    /// is malformed and the worker handler logs a warning and drops it
    /// (the same refusal shape <see cref="Exec"/> uses on empty
    /// <c>Command</c>).
    /// </summary>
    [ProtoMember(1)]
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// The role on the wire. Phase 1c only knows <c>"user"</c>; the
    /// v2 harness-spi surface extends the union to include
    /// <c>"system"</c> and <c>"tool"</c> without a wire change.
    /// </summary>
    [ProtoMember(2)]
    public string Role { get; init; } = "user";

    /// <summary>
    /// Free-form harness-specific hints. Treated as opaque by the
    /// worker; threaded to the harness's session transport verbatim.
    /// </summary>
    [ProtoMember(3)]
    public Dictionary<string, string> Metadata { get; init; } = [];
}
