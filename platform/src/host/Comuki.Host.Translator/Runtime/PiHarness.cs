using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Comuki.Host.Translator.Parsing;
using Comuki.Shared.Kernel.Harness;
using Microsoft.Extensions.Options;

namespace Comuki.Host.Translator.Runtime;

/// <summary>
/// Production <see cref="IHarnessRuntime"/> implementation: the
/// canonical harness is <c>pi</c> in <c>--mode rpc</c> session
/// mode (<c>openspec/changes/add-orchestra/spike-1b-report.md</c>).
/// <para>
/// <see cref="StartSessionAsync"/> spawns <c>pi --mode rpc</c> (the
/// v1.x one-shot <c>-p BRIEF --no-session</c> invocation does
/// not exist under this change — see
/// <c>openspec/changes/add-orchestra/specs/worker-runtime/spec.md</c>
/// MODIFIED "Agent invocation and stream parsing"), writes the
/// initial <c>prompt</c> JSON-RPC command with the run's brief,
/// and returns a <see cref="PiRpcSession"/> that owns the
/// <see cref="Process"/> + <see cref="StreamWriter"/> on
/// <c>stdin</c> + the <see cref="StreamReader"/> on <c>stdout</c>
/// + a reader background task that drains stdout line-by-line
/// and feeds the <see cref="PiEvent"/> channel.
/// </para>
/// <para>
/// Lifecycle: <see cref="Execution.Run.WorkerRun"/> awaits <c>StartSessionAsync</c>,
/// <see cref="Execution.Loop.PiPump"/> iterates the session's
/// <see cref="IHarnessSession.Events"/>, and the <c>await using</c>
/// on the session disposes on every exit path (success, cancellation,
/// pi crash). DisposeAsync closes the stdin writer (pi's documented
/// shutdown — close stdin → orderly exit, code 0), kills the
/// process tree on cancellation, and drains the exit task.
/// </para>
/// <para>
/// Concurrency: <see cref="ITurnInputWriter.TryWriteSteer"/> and
/// <see cref="ITurnInputWriter.TryWriteFollowUp"/> hold an
/// internal lock so the concurrent
/// <c>WorkerCommandHandler.HandleTurnInput</c> writer and the
/// reader task's flushing don't interleave on the same
/// <see cref="StreamWriter"/>. The reader task is the single
/// consumer of <c>stdout</c>; the worker run is the single
/// consumer of <see cref="IHarnessSession.Events"/>.
/// </para>
/// </summary>
/// <remarks>Builds the production harness. The runtime options hold the executable path and the working-directory default.</remarks>
/// <param name="options">Bound <c>Translator</c> section — <c>PiExecutable</c>, <c>WorkingDirectory</c>.</param>
/// <param name="logger">Logger for spawn / shutdown / write failures.</param>
public sealed class PiHarness(IOptions<TranslatorOptions> options, ILogger<PiHarness> logger) : IHarnessRuntime
{
    /// <inheritdoc />
    public string Name => "pi";

    /// <inheritdoc />
    public HarnessCapabilities Capabilities { get; } = new(liveSession: true);

    /// <summary>JSON-RPC <c>id</c> the initial <c>prompt</c> uses; correlates the <c>response</c> to this run.</summary>
    private const string InitialTurnIdPrefix = "p-";

    private readonly IOptions<TranslatorOptions> options = options;
    private readonly ILogger<PiHarness> logger = logger;

    /// <summary>Stable JSON-RPC id mint: monotonic per session, used by the worker-side command writer.</summary>
    private long nextRequestId;

    /// <inheritdoc />
    public Task<IHarnessSession> StartSessionAsync(
        HarnessStartRequest request,
        CancellationToken cancellationToken = default)
    {
        var executable = options.Value.PiExecutable;
        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
            WorkingDirectory = request.WorkingDirectory ?? options.Value.WorkingDirectory,
        };

        // pi 0.99.2 --mode rpc surface (per the spike): --mode rpc opens
        // the JSON-RPC-over-stdio channel; --no-session skips session
        // persistence (the v1.x one-shot flag, also dropped by this
        // change for session-capable runs); --session is a future handle,
        // the brief explicitly says we don't need it (one pi process per
        // cycle, owned by the same worker from prompt to stdin.close()).
        startInfo.ArgumentList.Add("--mode");
        startInfo.ArgumentList.Add("rpc");
        startInfo.ArgumentList.Add("--no-session");

        if (request.Environment is { Count: > 0 })
        {
            foreach (var (name, value) in request.Environment)
            {
                startInfo.Environment[name] = value;
            }
        }

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"failed to start '{executable}'");
        logger.LogInformation("Started {Executable} (--mode rpc) with PID {Pid}", executable, process.Id);

        // stream-json on stdout is the same wire shape as the
        // v1.x `--mode json` one-shot — `StreamJsonParser` is the same
        // parser (per the brief, untouched). The reader background task
        // drains the pipe and pushes parsed events into the channel;
        // the consumer is `PiPump` on the worker side.
        var events = System.Threading.Channels.Channel.CreateUnbounded<PiEvent>(
            new System.Threading.Channels.UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true,
            });

        var exitTaskSource = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stderrTask = PiProcessHelpers.DrainStderrAsync(process, cancellationToken);

        var readerTask = Task.Run(
            async () => await ReadEventsAsync(process.StandardOutput, events.Writer, cancellationToken),
            cancellationToken);

        _ = Task.Run(
            async () =>
            {
                try
                {
                    await process.WaitForExitAsync(CancellationToken.None);
                    exitTaskSource.TrySetResult(process.ExitCode);
                }
                catch (Exception exception)
                {
                    exitTaskSource.TrySetException(exception);
                }
            },
            CancellationToken.None);

        var writer = new PiRpcTurnInputWriter(process.StandardInput.BaseStream, logger);
        var initialTurnId = $"{InitialTurnIdPrefix}{Interlocked.Increment(ref nextRequestId)}";
        var initialWritten = writer.TryWritePrompt(initialTurnId, request.Brief);
        if (!initialWritten)
        {
            // The stdin pipe rejected the very first command; the
            // process is already dead. Fail-fast: dispose the process
            // tree and surface the broken state to the caller — the
            // pump's invalid-operation path will fail the item.
            logger.LogError(
                "Failed to write initial prompt to {Executable} stdin (PID {Pid})",
                executable,
                process.Id);
            events.Writer.TryComplete();
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception killException)
            {
                logger.LogWarning(killException, "failed to kill {Executable} (PID {Pid}) after initial-prompt failure", executable, process.Id);
            }

            throw new InvalidOperationException(
                $"'{executable}' (PID {process.Id}) closed stdin before the initial prompt landed");
        }

        return Task.FromResult<IHarnessSession>(new PiRpcSession(
            process.Id,
            events.Reader.ReadAllAsync(cancellationToken),
            writer,
            exitTaskSource.Task,
            readerTask,
            stderrTask,
            process,
            logger,
            executable));
    }

    private static async Task ReadEventsAsync(
        StreamReader stdout,
        System.Threading.Channels.ChannelWriter<PiEvent> writer,
        CancellationToken cancellationToken)
    {
        try
        {
            while (await stdout.ReadLineAsync(cancellationToken) is { } line)
            {
                foreach (var piEvent in StreamJsonParser.ParseLine(line))
                {
                    await writer.WriteAsync(piEvent, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // expected on session dispose — the channel's Complete() call below
            // flushes the consumer
        }
        finally
        {
            writer.TryComplete();
        }
    }
}

/// <summary>
/// One open <c>pi --mode rpc</c> session. Owns the <see cref="Process"/>
/// (one per worker run), the <see cref="StreamReader"/>
/// on stdout (consumed by the reader task that feeds the events
/// channel), the <see cref="StreamWriter"/> on stdin (consumed by
/// <see cref="PiRpcTurnInputWriter"/>), and a task for the
/// process exit code. <see cref="DisposeAsync"/> is the
/// single close path: stdin writer closed (pi's documented
/// shutdown), reader task awaited, stderr drained, process
/// tree-killed on cancellation, <see cref="Process"/>
/// disposed.
/// </summary>
internal sealed class PiRpcSession(
    int processId,
    IAsyncEnumerable<PiEvent> events,
    ITurnInputWriter turnInputs,
    Task<int> exitTask,
    Task readerTask,
    Task<string> stderrTask,
    Process process,
    ILogger logger,
    string executable) : IHarnessSession
{
    private readonly Task readerTask = readerTask;
    private readonly Task<string> stderrTask = stderrTask;
    private readonly Process process = process;
    private readonly ILogger logger = logger;
    private readonly string executable = executable;
    private readonly Lock disposeGate = new();
    private bool disposed;

    public int ProcessId { get; } = processId;

    public IAsyncEnumerable<PiEvent> Events { get; } = events;

    public ITurnInputWriter TurnInputs { get; } = turnInputs;

    public Task<int> ExitTask { get; } = exitTask;

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

        if (stderr.Length > 0)
        {
            logger.LogDebug("{Executable} (PID {Pid}) stderr: {Stderr}", executable, ProcessId, stderr);
        }

        process.Dispose();
    }
}

/// <summary>
/// Stdin-side writer for the <c>pi --mode rpc</c> channel. Each
/// command is one JSON object on its own line, terminated by LF
/// and flushed. A single lock guards the
/// <see cref="StreamWriter"/> so a concurrent steer and the
/// reader task's flush (or two steers) can't interleave mid-line.
/// <see cref="CloseStdin"/> is the orderly-shutdown close (the
/// session's dispose calls it; pi's
/// <c>openspec/changes/add-orchestra/spike-1b-report.md</c>
/// documents that closing stdin makes pi exit code 0).
/// </summary>
internal sealed class PiRpcTurnInputWriter : ITurnInputWriter
{
    private static readonly JsonSerializerOptions jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly StreamWriter writer;
    private readonly ILogger logger;
    private readonly Lock writeGate = new();
    private bool closed;

    public PiRpcTurnInputWriter(Stream stdin, ILogger logger)
    {
        writer = new StreamWriter(stdin, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = false,
            NewLine = "\n",
        };
        this.logger = logger;
    }

    public bool TryWriteSteer(string turnId, string text)
    {
        return TryWriteCommand(new { type = "steer", id = turnId, message = text });
    }

    public bool TryWriteFollowUp(string turnId, string text)
    {
        return TryWriteCommand(new { type = "follow_up", id = turnId, message = text });
    }

    public bool TryWritePrompt(string turnId, string text)
    {
        return TryWriteCommand(new { type = "prompt", id = turnId, message = text });
    }

    /// <summary>Closes the underlying <see cref="StreamWriter"/> (pi's orderly-shutdown signal).</summary>
    public void CloseStdin()
    {
        lock (writeGate)
        {
            if (closed)
            {
                return;
            }

            try
            {
                writer.Flush();
            }
            catch (Exception)
            {
                // best-effort: the close below is the actual shutdown signal
            }

            try
            {
                writer.Dispose();
            }
            catch (Exception)
            {
                // ignore — the process may already have died
            }

            closed = true;
        }
    }

    private bool TryWriteCommand(object command)
    {
        lock (writeGate)
        {
            if (closed)
            {
                return false;
            }

            try
            {
                // pi's stdin is JSON-RPC: one full JSON object per line.
                // .NET 10 dropped the JsonSerializer.Serialize(StreamWriter,
                // object?, JsonSerializerOptions?) overload (source-gen only);
                // serialize to the underlying Stream and keep the writer for
                // the trailing line terminator + flush. Writes hit the OS pipe
                // in order: JSON bytes immediately on BaseStream, '\n' on the
                // next writer.Flush(). The pi wire parser expects exactly
                // this — a single LF-terminated JSON object per command.
                JsonSerializer.Serialize(writer.BaseStream, command, jsonOptions);
                writer.Write('\n');
                writer.Flush();
                return true;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Failed to write {CommandType} command to pi stdin (PID context)",
                    command.GetType().Name);
                return false;
            }
        }
    }
}

/// <summary>Process plumbing kept out of the harness: stderr drain and read-loop setup.</summary>
file static class PiProcessHelpers
{
    public static Task<string> DrainStderrAsync(Process process, CancellationToken cancellationToken)
    {
        return Task.Run(
            async () =>
            {
                var buffer = new StringBuilder();
                while (await process.StandardError.ReadLineAsync(cancellationToken) is { } line)
                {
                    buffer.AppendLine(line);
                }

                return buffer.ToString();
            },
            cancellationToken);
    }
}
