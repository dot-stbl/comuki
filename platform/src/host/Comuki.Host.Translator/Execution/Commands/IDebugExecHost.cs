namespace Comuki.Host.Translator.Execution.Commands;

/// <summary>
/// One opt-in operator exec invocation the worker handler hands to the
/// host. Carries the spawn-time inputs the <see cref="IDebugExecHost"/>
/// needs: binary, verbatim arguments, working directory. The worker
/// handler is responsible for the <c>Translator:DebugExec</c> flag check;
/// implementations MUST assume the check has already passed — the
/// flag is the documented gate, this surface is the on-path only.
/// </summary>
/// <param name="Command">Binary to spawn verbatim, no shell.</param>
/// <param name="Arguments">Positional arguments, one child-argv slot per element.</param>
/// <param name="WorkingDirectory">Container cwd; null = inherit host cwd.</param>
public sealed record DebugExecRequest(
    string Command,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory);

/// <summary>
/// Result of one opt-in exec invocation. A launch failure maps to
/// <see cref="ExitCode"/> = null and a non-empty <see cref="FailureDetail"/>;
/// a successful launch maps to the captured child exit code (zero and
/// non-zero both count as success at the spawn boundary — the operator
/// is reading the exit code, the host is reading the spawn shape).
/// </summary>
/// <param name="ExitCode">Captured child exit code; null on launch failure.</param>
/// <param name="FailureDetail">Platform-agnostic failure reason; non-null on launch failure.</param>
public sealed record DebugExecOutcome(
    int? ExitCode,
    string? FailureDetail)
{
    /// <summary>True when the child failed to launch (no exit code was captured).</summary>
    public bool IsLaunchFailure => ExitCode is null;
}

/// <summary>
/// Worker-side opt-in operator exec surface (harden-pi-worker-sandbox
/// 5.3, spec "Operator debug is opt-in"). The handler only invokes
/// this when <c>Translator:DebugExec</c> is true; the flag check is
/// the handler's responsibility, not the host's. Implementations must
/// NOT leak this seam into the agent (pi) process — debug is an
/// operator surface, never a guest surface. The handler invocation
/// happens inside the Translator process boundary, the same one pi
/// runs under; the agent never gets a reference to this interface.
/// </summary>
public interface IDebugExecHost
{
    /// <summary>
    /// Spawns <paramref name="request"/>.<see cref="DebugExecRequest.Command"/>
    /// with the supplied arguments inside the worker boundary. The
    /// returned <see cref="DebugExecOutcome"/> carries the captured
    /// child exit code or a launch-failure detail; cancellation through
    /// <paramref name="cancellationToken"/> best-effort-kills the
    /// child and rethrows <see cref="OperationCanceledException"/>.
    /// </summary>
    /// <param name="request">Spawn-time inputs the handler resolved.</param>
    /// <param name="cancellationToken">Cancels the wait and best-effort-kills the child.</param>
    public Task<DebugExecOutcome> RunAsync(
        DebugExecRequest request,
        CancellationToken cancellationToken = default);
}
