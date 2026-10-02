namespace Comuki.Host.Translator.Execution.Verify;

/// <summary>
/// One verify-step outcome (isolate-verifier-runtime 1.1, spec scenario
/// "Verify uses the item's class"). Mirrors the restore outcome kind
/// so the loop can branch on the same shape: a success / skip pair lets
/// the loop close out the item cleanly; the failure pair carries a
/// single-line reason the loop forwards to <c>POST /workers/{id}/fail</c>
/// with the work item's stable reason.
/// </summary>
public enum VerifyOutcomeKind
{
    /// <summary>Every verify opcode exited zero; the item is reported as completed.</summary>
    Succeeded,

    /// <summary>No <c>[verify]</c> table was declared (or the table was empty) — verify is a no-op for this slot.</summary>
    Skipped,

    /// <summary>The toml's <c>class</c> disagrees with the worker's claimed class — class-mismatched claim.</summary>
    ClassMismatch,

    /// <summary>The toml failed to parse (closed-schema rejection, syntax, etc.).</summary>
    InvalidToml,

    /// <summary>An opcode exited non-zero, failed to launch, or hit the per-opcode timeout.</summary>
    OpcodeFailed,
}

/// <summary>
/// Verify-step outcome plus the human-readable reason the loop forwards
/// to <c>POST /workers/{id}/fail</c> when the kind is a failure kind.
/// <see cref="Opcodes"/> lists every opcode the runner executed in
/// catalog order — the journal payload (and the dashboard) use the list
/// to describe the verify scope without re-parsing the toml.
/// </summary>
/// <param name="Kind">How the step ended.</param>
/// <param name="Reason">Single-line failure reason; empty on success / skip.</param>
/// <param name="Opcodes">Every opcode the runner executed (catalog order).</param>
/// <param name="FailedOpcode">The opcode that exited non-zero on <see cref="VerifyOutcomeKind.OpcodeFailed"/>; empty otherwise.</param>
/// <param name="ExitCode">The non-zero exit code on <see cref="VerifyOutcomeKind.OpcodeFailed"/>; null otherwise.</param>
public sealed record VerifyOutcome(
    VerifyOutcomeKind Kind,
    string Reason,
    IReadOnlyList<string> Opcodes,
    string FailedOpcode,
    int? ExitCode);
