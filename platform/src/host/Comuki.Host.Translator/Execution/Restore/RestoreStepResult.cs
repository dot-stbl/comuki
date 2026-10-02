namespace Comuki.Host.Translator.Execution.Restore;

/// <summary>
/// Outcome of a single restore-opcode process invocation. ExitCode is the
/// child's exit code on success; null signals a launch failure (file not
/// found, timeout, denied) and <see cref="LaunchFailureDetail"/> carries
/// the human-readable reason — the loop decides what to fail the item
/// with.
/// </summary>
/// <param name="ExitCode">The child's exit code, or null on launch/timeout failure.</param>
/// <param name="LaunchFailureDetail">Set on a launch or timeout failure; null on normal exit.</param>
public sealed record RestoreStepResult(int? ExitCode, string? LaunchFailureDetail);
