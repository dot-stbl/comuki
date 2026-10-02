using System.Diagnostics;

namespace Comuki.Host.Translator.Execution.Clone;

/// <summary>
/// Real <see cref="ISourceCloneProcessRunner"/>: the same
/// <c>Process.Start(ProcessStartInfo)</c> shape as the restore runner —
/// no shell, best-effort kill on cancellation — plus the two things the
/// clone needs and restore does not: per-process environment overrides
/// and captured stderr (git's authentication and network errors land
/// there; the fail reason quotes them).
/// </summary>
public sealed class SourceCloneProcessRunner() : ISourceCloneProcessRunner
{
    /// <inheritdoc />
    public async Task<SourceCloneStepResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment,
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

        if (environment is { Count: > 0 })
        {
            foreach (var (name, value) in environment)
            {
                psi.Environment[name] = value;
            }
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
                return new SourceCloneStepResult(ExitCode: null, LaunchFailureDetail: "process failed to start", StandardError: string.Empty);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new SourceCloneStepResult(ExitCode: null, LaunchFailureDetail: $"launch failed: {exception.Message}", StandardError: string.Empty);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            // Both pipes are drained concurrently — a chatty remote that
            // fills either pipe buffer would otherwise deadlock the wait.
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            SourceCloneProcessRunnerHelpers.TryKill(process);
            throw;
        }

        await stdoutTask.WaitAsync(cancellationToken);
        var stderr = await stderrTask.WaitAsync(cancellationToken);
        return new SourceCloneStepResult(process.ExitCode, null, stderr);
    }
}

/// <summary>
/// File-scoped kill helper — best-effort on cancellation, never throws
/// (mirrors <c>RestoreProcessRunnerHelpers</c>; kept separate because the
/// two runners are separate compilation siblings by design).
/// </summary>
file static class SourceCloneProcessRunnerHelpers
{
    /// <summary>Best-effort tree kill on cancellation; OS reaps the child on disposal regardless.</summary>
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
            // as platform exceptions — a no-op here, the verdict does not
            // depend on whether the kill itself succeeded.
        }
    }
}
