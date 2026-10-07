using System.ComponentModel.DataAnnotations;

namespace Comuki.Modules.Verify.Application.Options;

/// <summary>
/// Per-project command that the Coda verification producer hands to the
/// generic-command verifier worker (add-orchestra §3 — Coda, task 3.2
/// "first registered provider is the existing Verify module"). The
/// producer step lives on <c>IVerificationGateProvider.EnsureGateRunAsync</c>
/// (default interface method on the SPI); the verify-module
/// GenericCommandGateProvider implements it and, when this section is
/// bound, inserts a <c>GenericCommandRun</c> for the work item on the
/// first evaluation pass. Subsequent evaluations are idempotent — the
/// partial index <c>ix_generic_command_runs_project_work_item</c>
/// covers the existence check.
/// </summary>
/// <remarks>
/// The default is empty / disabled: <see cref="Command"/> is <see cref="string.Empty"/>,
/// the producer no-ops, and the gate stays Pending — the same
/// "default-flipped-off" posture the verify module ships with.
/// Operators who want the producer to actually launch a command
/// populate <c>[Orchestration:Verification:CommandGate]</c> in
/// <c>config.toml</c>; cross-field validation in <see cref="Validate"/>
/// rejects a half-set pair (an empty <see cref="Command"/> alongside
/// populated <see cref="Arguments"/> or <see cref="ProfileKey"/>
/// surfaces as a boot-time validation error).
/// </remarks>
public sealed class CommandGateOptions : IValidatableObject
{
    /// <summary>Configuration section the host binds from.</summary>
    public const string SectionName = "Orchestration:Verification:CommandGate";

    /// <summary>
    /// Executable name. <see cref="string.Empty"/> (the default) keeps
    /// the producer off — the gate stamps Pending and the verifier
    /// worker has nothing to claim. A non-empty value pairs with a
    /// non-empty <see cref="ProfileKey"/> / <see cref="Arguments"/>
    /// (cross-field check in <see cref="Validate"/>).
    /// </summary>
    public string Command { get; init; } = string.Empty;

    /// <summary>
    /// Profile key the work item should resolve through the control plane.
    /// Bypassed when <see cref="string.Empty"/> (smoke tests, operator
    /// one-offs).
    /// </summary>
    public string ProfileKey { get; init; } = string.Empty;

    /// <summary>
    /// Positional arguments for <see cref="Command"/>. The runner passes the
    /// arguments to <c>ProcessStartInfo.ArgumentList</c> verbatim — no
    /// string concatenation, no shell re-parsing — so an argument
    /// containing spaces or shell metacharacters stays exactly one
    /// argument on the child's <c>argv</c>.
    /// </summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];

    /// <summary>
    /// Exit code that maps to a <c>Green</c> verdict — defaults to
    /// <c>0</c>, the convention every gate uses.
    /// </summary>
    [Range(0, 255)]
    public int ExpectedExitCode { get; init; }

    /// <summary>
    /// Cross-field validation: the section is all-or-nothing. An
    /// empty <see cref="Command"/> paired with populated
    /// <see cref="Arguments"/> / <see cref="ProfileKey"/> is the
    /// classic "operator half-edited the config" footgun — the
    /// producer would no-op while the operator thinks it is active.
    /// The reverse (a non-empty <see cref="Command"/> but empty
    /// <see cref="ProfileKey"/>) is the canonical operator-friendly
    /// shape (the bypass-the-control-plane smoke-test path).
    /// </summary>
    /// <param name="validationContext">Validation context the binder threads through.</param>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Command))
        {
            if (Arguments.Count > 0)
            {
                yield return new ValidationResult(
                    "Orchestration:Verification:CommandGate.Arguments is set but Command is empty — the section is all-or-nothing; leave Arguments unset when Command is empty.",
                    [nameof(Arguments)]);
            }

            if (!string.IsNullOrWhiteSpace(ProfileKey))
            {
                yield return new ValidationResult(
                    "Orchestration:Verification:CommandGate.ProfileKey is set but Command is empty — the section is all-or-nothing; leave ProfileKey unset when Command is empty.",
                    [nameof(ProfileKey)]);
            }
        }
    }
}
