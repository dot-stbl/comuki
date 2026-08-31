using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comuki.Host.Translator.Runtime;

/// <summary>
/// Default <see cref="IPiRunner"/>: spawns the configured executable with
/// <c>-p BRIEF --mode json --no-session</c> and yields stdout lines as
/// they arrive. Kills the whole process tree on cancellation (pi may have
/// spawned child node processes).
/// </summary>
/// <param name="options"></param>
/// <param name="logger"></param>
public sealed class PiRunner(
    IOptions<TranslatorOptions> options,
    ILogger<PiRunner> logger) : IPiRunner
{
    /// <inheritdoc />
    public async IAsyncEnumerable<string> RunAsync(
        string brief,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var executable = options.Value.PiExecutable;
        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = options.Value.WorkingDirectory,
        };
        startInfo.ArgumentList.Add("-p");
        startInfo.ArgumentList.Add(brief);
        startInfo.ArgumentList.Add("--mode");
        startInfo.ArgumentList.Add("json");
        startInfo.ArgumentList.Add("--no-session");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"failed to start '{executable}'");
        logger.LogInformation("Started {Executable} with PID {Pid}", executable, process.Id);

        var exitState = new PiExitState();
        var stderrTask = PiProcessHelpers.DrainStderrAsync(process, cancellationToken);

        try
        {
            while (await process.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
            {
                yield return line;
            }
        }
        finally
        {
            await PiProcessHelpers.TearDownAsync(process, stderrTask, exitState, logger, executable);
        }

        if (exitState.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"'{executable}' exited with code {exitState.ExitCode}. stderr: {exitState.Stderr}");
        }

        if (exitState.Stderr.Length > 0)
        {
            logger.LogDebug("{Executable} stderr: {Stderr}", executable, exitState.Stderr);
        }
    }
}

/// <summary>Exit facts of one pi run, filled by the teardown helper.</summary>
internal sealed class PiExitState
{
    public int ExitCode { get; set; }

    public string Stderr { get; set; } = string.Empty;
}

/// <summary>
/// Process plumbing kept out of the runner's iterator: concurrent stderr
/// drain (so the pipe never blocks stdout) and tree-kill teardown that
/// never throws from an iterator <c>finally</c>.
/// </summary>
internal static class PiProcessHelpers
{
    public static Task<string> DrainStderrAsync(Process process, CancellationToken cancellationToken)
    {
        return Task.Run(
            async () =>
            {
                var buffer = new System.Text.StringBuilder();
                while (await process.StandardError.ReadLineAsync(cancellationToken) is { } line)
                {
                    buffer.AppendLine(line);
                }

                return buffer.ToString();
            },
            cancellationToken);
    }

    public static async Task TearDownAsync(
        Process process,
        Task<string> stderrTask,
        PiExitState exitState,
        ILogger logger,
        string executable)
    {
        if (!process.HasExited)
        {
            logger.LogWarning("Cancelling {Executable} (PID {Pid})", executable, process.Id);
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (System.ComponentModel.Win32Exception exception)
            {
                logger.LogWarning(exception, "failed to kill process {Pid}", process.Id);
            }
            catch (InvalidOperationException exception)
            {
                logger.LogWarning(exception, "process {Pid} already exited", process.Id);
            }
        }

        try
        {
            exitState.Stderr = await stderrTask;
        }
        catch (OperationCanceledException)
        {
            exitState.Stderr = string.Empty;
        }

        await process.WaitForExitAsync(CancellationToken.None);
        exitState.ExitCode = process.ExitCode;
    }
}
