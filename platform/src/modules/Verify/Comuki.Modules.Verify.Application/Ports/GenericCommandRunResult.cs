namespace Comuki.Modules.Verify.Application.Ports;

/// <summary>
/// Outcome of a single <c>Process.Start</c> invocation the verifier
/// worker hands off to the runner. The runner — a container-isolated
/// runner is GH issue #47, deferred to v2 — fills this in; the worker
/// decides Green vs Red from the exit code alone.
/// </summary>
/// <param name="ExitCode">
/// Exit code reported by <c>Process.ExitCode</c>. Null when the process
/// failed to launch at all (file not found, permission denied, timeout
/// before exit).
/// </param>
/// <param name="OutputLog">
/// Captured stdout+stderr (interleaved in stream order), truncated by
/// the runner to the configured soft cap.
/// </param>
/// <param name="LaunchFailureDetail">
/// Short failure reason when the process could not be launched or timed
/// out. The runner sets this; the worker maps it to Red with the same
/// payload in <c>output_log</c>.
/// </param>
public readonly record struct GenericCommandRunResult(
    int? ExitCode,
    string OutputLog,
    string? LaunchFailureDetail)
{
    /// <summary>True when the runner could not launch the process (or it never exited in time).</summary>
    public bool LaunchFailed => ExitCode is null;
}
