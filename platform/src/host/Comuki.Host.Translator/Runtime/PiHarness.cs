using System.Diagnostics;
using System.Text;
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
/// shutdown — close stdin → orderly exit, code 0) and kills the
/// process tree on cancellation.
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
        // the JSON-RPC-over-stdio channel; --no-session is the same
        // flag the v1.x one-shot form used to pass, and is still
        // passed under rpc — under rpc the flag disables session
        // persistence (the process lives for the worker's lifetime
        // either way; the v1.x meaning of "no session at all" no
        // longer applies because rpc IS the session); --session is a
        // future handle, the brief explicitly says we don't need it
        // (one pi process per cycle, owned by the same worker from
        // prompt to stdin.close()).
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

        var stderrTask = PiProcessHelpers.DrainStderrAsync(process, cancellationToken);

        var readerTask = Task.Run(
            async () => await PiReader.ReadEventsAsync(process.StandardOutput, events.Writer, cancellationToken),
            cancellationToken);

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
            readerTask,
            stderrTask,
            process,
            logger,
            executable));
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

/// <summary>
/// Pure reader for the harness's <c>stdout</c>: line-by-line drain
/// into the events channel. File-static so it carries no instance
/// fields of <see cref="PiHarness"/>.
/// </summary>
file static class PiReader
{
    /// <summary>
    /// Drains <paramref name="stdout"/> into <paramref name="writer"/>
    /// until the stream closes or cancellation trips. Each line is
    /// parsed by <see cref="StreamJsonParser.ParseLine"/> (the same
    /// parser the v1.x one-shot path uses); the channel's
    /// <c>Complete()</c> in the <c>finally</c> below flushes the
    /// consumer on shutdown.
    /// </summary>
    /// <param name="stdout">The harness's stdout.</param>
    /// <param name="writer">The events channel writer.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public static async Task ReadEventsAsync(
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
