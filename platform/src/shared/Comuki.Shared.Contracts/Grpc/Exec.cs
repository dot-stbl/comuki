using ProtoBuf;

namespace Comuki.Shared.Contracts.Grpc;

/// <summary>
/// Opt-in operator exec (harden-pi-worker-sandbox 5.3, spec "Operator
/// debug is opt-in"): an authenticated operator asks the worker to spawn
/// a child process inside its own boundary. The flag that gates this is
/// held by the worker (<c>Translator:DebugExec</c>, default off) — an
/// <see cref="Exec"/> command received while the flag is off is refused
/// and logged but never executed. Debug is an operator surface; the
/// pi (agent) process has no path to it.
/// </summary>
[ProtoContract]
public sealed record Exec
{
    /// <summary>
    /// Binary to spawn verbatim, no shell. Workers MUST treat an empty
    /// <see cref="Command"/> as malformed and refuse it the same way
    /// the flag is off (the flag is the documented gate; an empty
    /// command is the documented misroute).
    /// </summary>
    [ProtoMember(1)]
    public string Command { get; init; } = string.Empty;

    /// <summary>
    /// Positional arguments, one child-argv slot per element. The
    /// worker passes these through
    /// <see cref="System.Diagnostics.ProcessStartInfo.ArgumentList"/>
    /// so spaces, quotes and shell metacharacters in a target land on
    /// the child argv as one literal (mirrors the restore / verify
    /// process-runner shape; the same shell-injection surface applies
    /// to the operator side).
    /// </summary>
    [ProtoMember(2)]
    public IReadOnlyList<string> Arguments { get; init; } = [];
}
