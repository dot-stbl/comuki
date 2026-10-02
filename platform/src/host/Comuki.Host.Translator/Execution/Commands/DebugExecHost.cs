using System.Diagnostics;

namespace Comuki.Host.Translator.Execution.Commands;

/// <summary>
/// Real <see cref="IDebugExecHost"/>: same <c>Process.Start(ProcessStartInfo)</c>
/// shape as <see cref="Restore.IRestoreProcessRunner"/> — no shell,
/// redirected stdout+stderr, cancellation through a linked
/// <see cref="CancellationTokenSource"/>, best-effort kill on cancel.
/// Mirroring the restore runner (rather than reusing it directly) keeps
/// this surface independent of the after-clone restore path: the flag
/// is on the operator side, the test seam is in this file, the process
/// shape matches by intent not by inheritance. The agent (pi) never
/// reaches this — the handler is the only caller, and the handler runs
/// in the Translator process.
/// </summary>
public sealed class DebugExecHost : IDebugExecHost
{
    /// <inheritdoc />
    public async Task<DebugExecOutcome> RunAsync(
        DebugExecRequest request,
        CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = request.Command,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            CreateNoWindow = true,
        };

        foreach (var argument in request.Arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
        {
            psi.WorkingDirectory = request.WorkingDirectory;
        }

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        try
        {
            if (!process.Start())
            {
                return new DebugExecOutcome(ExitCode: null, FailureDetail: "process failed to start");
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new DebugExecOutcome(ExitCode: null, FailureDetail: $"launch failed: {exception.Message}");
        }

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            DebugExecHostHelpers.TryKill(process);
            throw;
        }

        return new DebugExecOutcome(ExitCode: process.ExitCode, FailureDetail: null);
    }
}

/// <summary>
/// File-scoped kill helper for <see cref="DebugExecHost"/> —
/// best-effort on cancellation, never throws (the OS reaps the child on
/// disposal regardless). Pure orchestration over <see cref="Process"/>,
/// so it lives outside the class per the class-layout-and-tooling rule.
/// </summary>
file static class DebugExecHostHelpers
{
    /// <summary>
    /// Best-effort kill — the OS reaps the child on disposal either way,
    /// and the operator-side verdict does not depend on whether the
    /// kill itself succeeded. Errors are swallowed silently so the host
    /// surface stays free of Win32 types.
    /// </summary>
    public static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // boundary: Process.Kill surfaces invalid handle / not-supported
            // as platform exceptions — translated to a no-op, the host
            // surface stays free of Win32 types.
        }
    }
}
