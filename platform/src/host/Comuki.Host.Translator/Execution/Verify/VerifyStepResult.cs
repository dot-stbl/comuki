namespace Comuki.Host.Translator.Execution.Verify;

/// <summary>
/// One <c>Process.Start</c> invocation: a worker-side child process spawned
/// by <see cref="VerifyRunner"/> for a single verify opcode. Mirrors the
/// restore runner's <see cref="Restore.IRestoreProcessRunner"/> shape
/// exactly (no shell, no host cwd, every argument through
/// <see cref="System.Diagnostics.ProcessStartInfo.ArgumentList"/>) so the
/// two runners share the same ProcessRunner seam — tests inject a single
/// fake for both, and the verifier never grows its own
/// <c>Process.Start</c> branch.
/// </summary>
public sealed record VerifyStepResult(int? ExitCode, string? LaunchFailureDetail);
