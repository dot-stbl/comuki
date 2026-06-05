using System.Diagnostics;
using System.Runtime.CompilerServices;
using Comuki.Platform.Worker.Translator.Interfaces;
using Microsoft.Extensions.Logging;

namespace Comuki.Platform.Worker.Translator;

/// <summary>
/// Default <see cref="IPiRunner"/> implementation. Spawns the configured executable
/// (production: <c>pi</c>; tests: <c>TestFakePi</c>) with stream-json flags and
/// yields stdout lines as they arrive. Cancels the process on
/// <see cref="CancellationToken"/> cancellation.
/// </summary>
/// <remarks>
/// Why <c>Process.Start</c> and not <c>CliWrap</c> or <c>MedallionShell</c>:
/// we want minimum dependencies in the worker image (just <c>pi</c> + .NET runtime).
/// <c>Process.Start</c> is BCL, AOT-friendly, no extra packaging cost. Cancellation
/// via <c>Process.Kill(entireProcessTree: true)</c> because <c>pi</c> may have
/// spawned a child Node process.
/// </remarks>
public sealed class PiRunner : IPiRunner
{
    private readonly string executable;
    private readonly ILogger<PiRunner> logger;

    public PiRunner(string executable, ILogger<PiRunner> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(logger);
        this.executable = executable;
        this.logger = logger;
    }

    public async IAsyncEnumerable<string> RunAsync(
        string prompt,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(prompt);

        var psi = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add(prompt);
        psi.ArgumentList.Add("--output-format");
        psi.ArgumentList.Add("stream-json");

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start '{executable}'");

        logger.LogInformation("Started {Executable} with PID {Pid}", executable, process.Id);

        var stderrTask = DrainStderrAsync(process, cancellationToken);

        var exitState = new ExitState();
        try
        {
            while (await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                yield return line;
            }
        }
        finally
        {
            await DisposeProcessAsync(process, stderrTask, exitState, cancellationToken).ConfigureAwait(false);
        }

        if (exitState.ExitCode != 0)
        {
            logger.LogError(
                "{Executable} exited with code {ExitCode}. stderr: {Stderr}",
                executable, exitState.ExitCode, exitState.Stderr);
            throw new InvalidOperationException(
                $"{executable} exited with code {exitState.ExitCode}. stderr: {exitState.Stderr}");
        }

        if (exitState.Stderr.Length > 0)
        {
            logger.LogDebug("{Executable} stderr: {Stderr}", executable, exitState.Stderr);
        }
    }

    /// <summary>
    /// Drains stderr concurrently so the pipe doesn't fill and block stdout reads.
    /// </summary>
    private static Task<string> DrainStderrAsync(Process process, CancellationToken cancellationToken)
        => Task.Run(async () =>
        {
            var buffer = new System.Text.StringBuilder();
            string? line;
            while ((line = await process.StandardError.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
            {
                buffer.AppendLine(line);
            }

            return buffer.ToString();
        }, cancellationToken);

    /// <summary>
    /// Kills the process if still running, waits for exit, captures stderr and
    /// exit code. Throws are caught and logged so the iterator's <c>finally</c>
    /// never rethrows (MA0072).
    /// </summary>
    private async Task DisposeProcessAsync(Process process, Task<string> stderrTask, ExitState exitState, CancellationToken cancellationToken)
    {
        if (!process.HasExited)
        {
            logger.LogWarning("Cancelling {Executable} (PID {Pid})", executable, process.Id);
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to kill process {Pid}", process.Id);
            }
        }

        try
        {
            exitState.Stderr = await stderrTask.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to drain stderr for {Pid}", process.Id);
        }

        try
        {
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "WaitForExit failed for {Pid}", process.Id);
        }

        exitState.ExitCode = process.ExitCode;
    }

    private sealed class ExitState
    {
        public int ExitCode { get; set; }

        public string Stderr { get; set; } = string.Empty;
    }
}
