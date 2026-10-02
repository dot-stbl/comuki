using System.Diagnostics;

namespace Comuki.Host.Translator.Execution.Restore;

/// <summary>
/// Real <see cref="IRestoreProcessRunner"/>: the same <c>Process.Start(ProcessStartInfo)</c>
/// shape as the Verify module's command runner — no shell, redirected
/// stdout+stderr, cancellation through a linked <see cref="CancellationTokenSource"/>,
/// best-effort kill on cancel. The Translator is a separate compilation
/// unit from <c>Comuki.Modules.Verify</c> and cannot reach into the
/// runner there; this duplicate is the seam tests substitute with a fake.
/// </summary>
public sealed class RestoreProcessRunner() : IRestoreProcessRunner
{
    /// <inheritdoc />
    public async Task<RestoreStepResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            psi.WorkingDirectory = workingDirectory;
        }

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        try
        {
            if (!process.Start())
            {
                return new RestoreStepResult(ExitCode: null, LaunchFailureDetail: "process failed to start");
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new RestoreStepResult(ExitCode: null, LaunchFailureDetail: $"launch failed: {exception.Message}");
        }

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            RestoreProcessRunnerHelpers.TryKill(process);
            throw;
        }

        return new RestoreStepResult(ExitCode: process.ExitCode, LaunchFailureDetail: null);
    }
}

/// <summary>
/// File-scoped kill helper for <see cref="RestoreProcessRunner"/> —
/// best-effort on cancellation, never throws (the OS reaps the child on
/// disposal regardless). Pure orchestration over <see cref="Process"/>,
/// so it lives outside the class per <c>class-layout-and-tooling.md</c>
/// §1a (no private methods).
/// </summary>
file static class RestoreProcessRunnerHelpers
{
    /// <summary>
    /// Best-effort kill — the OS reaps the child on disposal either way,
    /// and the caller's verdict does not depend on whether the kill itself
    /// succeeded. Errors are swallowed silently so the runner's surface
    /// stays free of Win32 types (the boundary comment in
    /// <see cref="RestoreProcessRunner.RunAsync"/> is what the contract
    /// relies on).
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
            // as platform exceptions — translated to a no-op, the runner
            // surface stays free of Win32 types.
        }
    }
}
