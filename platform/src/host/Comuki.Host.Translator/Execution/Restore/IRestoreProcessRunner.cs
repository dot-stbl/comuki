namespace Comuki.Host.Translator.Execution.Restore;

/// <summary>
/// One <c>Process.Start</c> invocation: a host-side child process spawned
/// by <see cref="RestoreRunner"/> for a single restore opcode. Mirrors the
/// Verify module's <c>GenericCommandProcessRunner</c> shape (no shell,
/// <c>UseShellExecute=false</c>, every argument passed individually through
/// <see cref="System.Diagnostics.ProcessStartInfo.ArgumentList"/>)
/// so spaces, quotes, and shell metacharacters in a target path land on
/// the child's <c>argv</c> as one inert literal. The Translator cannot
/// reuse the Verify runner directly — the Verify module is a host-side
/// feature module, not a shared primitive — so the same shape is
/// implemented here, behind this interface, with the test seam factored
/// out.
/// </summary>
public interface IRestoreProcessRunner
{
    /// <summary>
    /// Launches <paramref name="executable"/> with <paramref name="arguments"/>,
    /// waits for it, captures the exit code, and returns a structured
    /// outcome. Implementations MUST NOT go through a shell — arguments
    /// are passed verbatim.
    /// </summary>
    /// <param name="executable">The executable to launch.</param>
    /// <param name="arguments">Positional arguments, passed as an array.</param>
    /// <param name="workingDirectory">Optional cwd; null means inherit host cwd.</param>
    /// <param name="cancellationToken">Cancels the wait and best-effort-kills the child.</param>
    public Task<RestoreStepResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken = default);
}
