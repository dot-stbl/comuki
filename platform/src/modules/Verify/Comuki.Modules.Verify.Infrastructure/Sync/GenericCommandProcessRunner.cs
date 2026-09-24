using System.Diagnostics;
using Comuki.Modules.Verify.Application.Options;
using Comuki.Modules.Verify.Application.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comuki.Modules.Verify.Infrastructure.Sync;

/// <summary>
/// In-process <see cref="IGenericCommandRunner"/>: launches
/// <c>executable</c> with <c>arguments</c> via <c>Process.Start</c>,
/// captures stdout+stderr in stream order, enforces the configured
/// timeout + output cap, and returns the raw outcome. The runner never
/// decides the verdict — that is the worker's call from the exit code
/// alone.
/// <para>
/// <b>No shell, ever.</b> <see cref="ProcessStartInfo.UseShellExecute"/>
/// is always <see langword="false"/> and every argument is appended to
/// <see cref="ProcessStartInfo.ArgumentList"/> individually — never
/// concatenated into a single string. That is what makes an argument
/// containing spaces, quotes, or shell metacharacters (<c>;</c>,
/// <c>&amp;&amp;</c>, <c>|</c>, backticks) land on the child's
/// <c>argv</c> as one inert literal instead of being re-parsed by a
/// shell (cross-platform.md §8).
/// </para>
/// <para>
/// <b>Isolation warning.</b> "No shell" is not "sandboxed" — the child
/// still runs with the orchestrator host's OS-level permissions, network
/// access, and filesystem visibility (the same class of gap
/// <c>openspec/changes/harden-pi-worker-sandbox</c> closes for worker
/// containers). A container-isolated runner behind this same
/// <see cref="IGenericCommandRunner"/> port is GH issue #47, deferred to
/// v2; until then <see cref="VerifyOptions.Enabled"/> defaults to
/// <see langword="false"/> and this runner MUST only be enabled for
/// executables the deployer already trusts.
/// </para>
/// </summary>
/// <param name="options">Bound from <c>Verify:Verifier</c>.</param>
/// <param name="logger">Structured logger — Warning on timeout / output overflow.</param>
public sealed class GenericCommandProcessRunner(
    IOptions<VerifyOptions> options,
    ILogger<GenericCommandProcessRunner> logger) : IGenericCommandRunner
{
    /// <inheritdoc />
    public async Task<GenericCommandRunResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken = default)
    {
        var timeout = options.Value.RunTimeout;
        var cap = options.Value.OutputLogCharCap;

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

        var capture = new OutputCapture(cap, logger);
        process.OutputDataReceived += (_, args) => capture.AppendLine(args.Data, isError: false);
        process.ErrorDataReceived += (_, args) => capture.AppendLine(args.Data, isError: true);

        try
        {
            if (!process.Start())
            {
                return new GenericCommandRunResult(
                    ExitCode: null,
                    OutputLog: string.Empty,
                    LaunchFailureDetail: "process failed to start");
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // boundary: the runner translates a hard launch failure
            // (file-not-found, access denied — including an executable
            // string that only looks like a shell command line, since
            // UseShellExecute=false means it is looked up literally) into
            // a typed outcome — the worker does not see raw Win32
            // exceptions.
            return new GenericCommandRunResult(
                ExitCode: null,
                OutputLog: string.Empty,
                LaunchFailureDetail: $"launch failed: {exception.Message}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Host shutdown honours the linked token; the host-level
            // token cancels through the worker. A timeout cancels the
            // child best-effort and surfaces as Red.
            logger.LogWarning(
                "generic-command runner timed out after {TimeoutSeconds}s; killing process {ProcessId}",
                timeout.TotalSeconds,
                ProcessHelpers.SafeProcessId(process));

            ProcessHelpers.TryKill(process, logger);
            capture.AppendLine($"[runner] command killed after timeout of {timeout.TotalSeconds:0.#}s", isError: true);

            return new GenericCommandRunResult(
                ExitCode: null,
                OutputLog: capture.Snapshot(),
                LaunchFailureDetail: $"runner timeout after {timeout.TotalSeconds:0.#}s");
        }
        catch (OperationCanceledException)
        {
            ProcessHelpers.TryKill(process, logger);
            throw;
        }

        return new GenericCommandRunResult(
            ExitCode: process.ExitCode,
            OutputLog: capture.Snapshot(),
            LaunchFailureDetail: null);
    }
}

/// <summary>
/// File-scoped helpers for <see cref="GenericCommandProcessRunner"/> —
/// process-id and best-effort-kill are pure orchestration over
/// <see cref="Process"/>, not entity behaviour, so they live outside the
/// class per <c>class-layout-and-tooling.md</c> §1a (no private methods).
/// </summary>
file static class ProcessHelpers
{
    /// <summary>
    /// Reads <see cref="Process.Id"/> defensively — the property throws
    /// once the process has already exited, and this is only ever used
    /// for a log message, not a control-flow decision.
    /// </summary>
    public static int SafeProcessId(Process process)
    {
        try
        {
            return process.Id;
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }

    /// <summary>
    /// Best-effort kill on timeout/cancellation — the OS reaps the child
    /// on disposal either way, and the caller's verdict does not depend
    /// on whether the kill itself succeeded. Logged (not silently
    /// swallowed) so a persistently un-killable child stays visible.
    /// </summary>
    public static void TryKill(Process process, ILogger logger)
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
            logger.LogDebug(exception, "best-effort kill failed for process {ProcessId}", SafeProcessId(process));
        }
    }
}
