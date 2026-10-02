using ProtoBuf;

namespace Comuki.Shared.Contracts.Grpc;

/// <summary>
/// Verify outcome event (isolate-verifier-runtime 1.4, spec scenario
/// "Build failure is a verify report"). Carries one of two statuses:
/// <c>completed</c> when every declared verify opcode exited zero, or
/// <c>failed</c> when an opcode exited non-zero or failed to launch.
/// The host-side journal mapping turns this into
/// <c>verify.completed</c> / <c>verify.failed</c> entries with the
/// opcode list / opcode+exit-code payload.
/// </summary>
[ProtoContract]
public sealed record StageVerify
{
    /// <summary>Work item id this verify event belongs to (matches the claim).</summary>
    [ProtoMember(1)]
    public string WorkItemId { get; init; } = string.Empty;

    /// <summary><c>completed</c> when every opcode exited zero; <c>failed</c> otherwise.</summary>
    [ProtoMember(2)]
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// All opcodes the runner executed (in catalog order). Present on
    /// both statuses — the journal can read the duration / scope of
    /// the verify run without re-parsing the toml.
    /// </summary>
    [ProtoMember(3)]
    public IReadOnlyList<string> Opcodes { get; init; } = [];

    /// <summary>
    /// Set on <c>failed</c>: the one opcode whose exit was non-zero (or
    /// whose launch failed). Empty on <c>completed</c>.
    /// </summary>
    [ProtoMember(4)]
    public string FailedOpcode { get; init; } = string.Empty;

    /// <summary>
    /// Set on <c>failed</c>: the process exit code the runner reported.
    /// Null on launch failure and on <c>completed</c>.
    /// </summary>
    [ProtoMember(5)]
    public long? ExitCode { get; init; }

    /// <summary>
    /// Short human reason — the same string the runner's <c>Reason</c>
    /// carries (e.g. <c>"verify opcode 'dotnet' for 'comuki.slnx' exited 2"</c>).
    /// Empty on success.
    /// </summary>
    [ProtoMember(6)]
    public string Reason { get; init; } = string.Empty;
}
