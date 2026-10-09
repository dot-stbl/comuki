using System.Diagnostics;
using Comuki.Host.Translator.Parsing;

namespace Comuki.Host.Translator.Runtime;

/// <summary>
/// One open <c>pi --mode rpc</c> session. Owns the <see cref="Process"/>
/// (one per worker run), the <see cref="StreamReader"/>
/// on stdout (consumed by the reader task that feeds the events
/// channel), and the <see cref="StreamWriter"/> on stdin (consumed by
/// <see cref="PiRpcTurnInputWriter"/>). <see cref="DisposeAsync"/> is
/// the single close path: stdin writer closed (pi's documented
/// shutdown), reader task awaited, stderr drained, process
/// tree-killed on cancellation, <see cref="Process"/> disposed.
/// </summary>
internal sealed class PiRpcSession(
    int processId,
    IAsyncEnumerable<PiEvent> events,
    ITurnInputWriter turnInputs,
    Task readerTask,
    Task<string> stderrTask,
    Process process,
    ILogger logger,
    string executable) : IHarnessSession
{
    /// <summary>
    /// Safe cap on the captured stderr tail exposed via
    /// <see cref="IHarnessSession.StderrTail"/>. The full stderr is
    /// captured by <c>PiProcessHelpers.DrainStderrAsync</c>; this cap
    /// keeps the surface area on a non-zero exit small (4 KiB — enough
    /// for a couple hundred lines of pi diagnostics, well below the
    /// payload limit of the journal's worker.reported event). The tail
    /// is the last <see cref="StderrTailMaxChars"/> characters, not the
    /// head, because the failing line is the one that lands at the end.
    /// </summary>
    public const int StderrTailMaxChars = 4096;

    private readonly Lock disposeGate = new();
    private bool disposed;

    public int ProcessId { get; } = processId;

    /// <inheritdoc />
    public int? ExitCode { get; private set; }

    /// <inheritdoc />
    public string? StderrTail { get; private set; }

    public IAsyncEnumerable<PiEvent> Events { get; } = events;

    public ITurnInputWriter TurnInputs { get; } = turnInputs;

    public async ValueTask DisposeAsync()
    {
        lock (disposeGate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
        }

        // Order matters: close the writer first so pi's orderly-shutdown
        // path sees EOF on stdin (the documented shutdown). Only then
        // tree-kill if the process is still alive — the EOF should make
        // the wait-for-exit return on its own; the kill is a backstop
        // for cancellation cases where the wait is interrupted before
        // the process notices the close.
        if (TurnInputs is PiRpcTurnInputWriter rpcWriter)
        {
            try
            {
                rpcWriter.CloseStdin();
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "failed to close stdin on {Executable} (PID {Pid})", executable, ProcessId);
            }
        }

        try
        {
            await readerTask;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogDebug(exception, "reader task for {Executable} (PID {Pid}) ended with an exception", executable, ProcessId);
        }

        string stderr;
        try
        {
            stderr = await stderrTask;
        }
        catch (OperationCanceledException)
        {
            stderr = string.Empty;
        }

        if (!process.HasExited)
        {
            logger.LogWarning("Cancelling {Executable} (PID {Pid})", executable, ProcessId);
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (System.ComponentModel.Win32Exception exception)
            {
                logger.LogWarning(exception, "failed to kill {Executable} (PID {Pid})", executable, ProcessId);
            }
            catch (InvalidOperationException exception)
            {
                logger.LogWarning(exception, "{Executable} (PID {Pid}) already exited", executable, ProcessId);
            }
        }

        try
        {
            await process.WaitForExitAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "wait-for-exit on {Executable} (PID {Pid}) ended with an exception", executable, ProcessId);
        }

        // ExitCode is now the authoritative OS-level code (zero on a
        // clean shutdown via stdin close, non-zero on any crash Test
        // — pi could be triggered either way). Spec surfaced for a
        // non-zero value above; capture regardless so callers can read
        // it (Loop.PiPump consumes this after DisposeAsync returns).
        // Guarded by HasExited because Process.ExitCode throws
        // InvalidOperationException on a process that has not exited —
        // a defensive backstop in case the wait above didn't drain the
        // child for any reason.
        ExitCode = process.HasExited ? process.ExitCode : null;

        // Surface the stderr tail to the consumer (PiPump appends it to
        // the outcome's ErrorText on a non-zero exit). Full stderr is
        // logged at debug — the tail is the operator-facing slice.
        if (stderr.Length > 0)
        {
            StderrTail = stderr.Length > StderrTailMaxChars
                ? stderr[^StderrTailMaxChars..]
                : stderr;
            logger.LogDebug("{Executable} (PID {Pid}) stderr: {Stderr}", executable, ProcessId, stderr);
        }

        process.Dispose();
    }
}
