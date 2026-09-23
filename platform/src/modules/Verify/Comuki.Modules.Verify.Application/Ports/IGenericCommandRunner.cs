namespace Comuki.Modules.Verify.Application.Ports;

/// <summary>
/// The boundary between the worker (orchestration) and the process
/// launcher. Production wires the in-process runner
/// (<c>GenericCommandProcessRunner</c>) — v1.1 fleet work (GH issue #47)
/// will swap in a container-isolated runner behind this same port; the
/// worker never changes. The interface lives in Application because the
/// worker decides the verdict and the runner just returns raw numbers —
/// neither layer reaches into the other.
/// </summary>
public interface IGenericCommandRunner
{
    /// <summary>
    /// Launches <paramref name="executable"/> with <paramref name="arguments"/>,
    /// waits for it to exit, captures stdout+stderr, and returns the
    /// outcome. The runner enforces its own timeout and output-cap
    /// policy. Implementations MUST NOT go through a shell — arguments
    /// are passed as an array (e.g. <c>ProcessStartInfo.ArgumentList</c>),
    /// never concatenated into a command-line string.
    /// </summary>
    /// <param name="executable">The executable to launch — never a shell command line.</param>
    /// <param name="arguments">Positional arguments, passed verbatim (no re-parsing).</param>
    /// <param name="workingDirectory">
    /// Optional cwd for the child process. Null means "inherit host cwd".
    /// </param>
    /// <param name="cancellationToken">Cancels the wait and (best-effort) the child.</param>
    public Task<GenericCommandRunResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken = default);
}
