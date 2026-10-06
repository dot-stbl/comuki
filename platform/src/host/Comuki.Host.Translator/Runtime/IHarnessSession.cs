using Comuki.Host.Translator.Parsing;

namespace Comuki.Host.Translator.Runtime;

/// <summary>
/// Runtime half of the harness SPI (add-orchestra Phase 1c). The
/// shared kernel's <see cref="Shared.Kernel.Harness.IHarness"/>
/// declares capabilities; this interface declares the spawn side.
/// The Translator's <c>PiPump</c> reads
/// <see cref="StartSessionAsync"/>; the <c>HarnessRegistry</c> on
/// the host side keys by <see cref="Shared.Kernel.Harness.IHarness.Name"/>.
/// <para>
/// The split exists because the runtime layer's
/// <see cref="PiEvent"/> type
/// is a Translator concern; the shared kernel deliberately does
/// not depend on the Translator. The Translator injects this
/// interface; the host's harness resolver injects the
/// capability-only half.
/// </para>
/// </summary>
public interface IHarnessRuntime : Shared.Kernel.Harness.IHarness
{
    /// <summary>
    /// Spawns a new harness session for one claimed work item.
    /// The returned <see cref="IHarnessSession"/> owns the
    /// process (production: the <c>pi --mode rpc</c> process;
    /// tests: an in-process fake). The caller <c>await using</c>'s
    /// the session — disposal closes stdin, kills the process
    /// tree on cancellation, and drains the reader task.
    /// </summary>
    /// <param name="request">The brief, env stamps, and working directory for this session.</param>
    /// <param name="cancellationToken">Token propagated to the spawn; cancellation aborts before the process is started.</param>
    public Task<IHarnessSession> StartSessionAsync(
        HarnessStartRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// One live session with a harness process (production: the
/// <c>pi --mode rpc</c> process; tests: the in-process
/// <c>TestFakeHarness</c> echo). The session is the seam the
/// Translator's <c>PiPump</c> reads events from and the
/// <c>WorkerCommandHandler</c> writes
/// <see cref="Shared.Contracts.Grpc.TurnInput"/> turns into.
/// Closing the session (via <see cref="IAsyncDisposable.DisposeAsync"/>)
/// signals orderly shutdown: the writer flushes its last command,
/// the writer is closed, the harness process sees EOF on stdin and
/// exits; the reader drains the remaining events; the session's
/// <see cref="ExitTask"/> resolves.
/// <para>
/// Concurrency: the <see cref="Events"/> reader and the
/// <see cref="TurnInputs"/> writer are safe to drive in parallel
/// (the writer is lock-protected; the reader is a single-consumer
/// async iterator over the harness's stdout). One session per
/// <see cref="Execution.Run.WorkerRun"/>; the worker run owns disposal.
/// </para>
/// </summary>
public interface IHarnessSession : IAsyncDisposable
{
    /// <summary>OS-level process id; useful for log correlation and orphaned-process scans.</summary>
    public int ProcessId { get; }

    /// <summary>
    /// The stream of <see cref="PiEvent"/>s the harness emits on
    /// stdout (or, for the in-process test fake, the synthesised
    /// echo of each command). Cancellation trips the iterator
    /// without killing the harness process — the process is killed
    /// on <see cref="IAsyncDisposable.DisposeAsync"/>.
    /// </summary>
    public IAsyncEnumerable<PiEvent> Events { get; }

    /// <summary>
    /// The stdin-side writer. <c>steer</c> is the mid-flight
    /// delivery; <c>follow_up</c> is the post- <c>agent_settled</c>
    /// delivery (semantics per
    /// <c>openspec/changes/add-orchestra/spike-1b-report.md</c>).
    /// <c>TryWrite*</c> returns <c>false</c> when the harness
    /// stream is closed (process already exited or session
    /// disposed) — the caller logs and drops, the same refusal
    /// shape <c>WorkerCommandHandler.HandleTurnInput</c> uses for
    /// empty-text turns.
    /// </summary>
    public ITurnInputWriter TurnInputs { get; }

    /// <summary>
    /// Resolves with the harness's process exit code when the
    /// process ends (clean shutdown, crash, or kill). The
    /// <c>PiPump</c> reads this to know when the cycle is over
    /// without having to race the event stream.
    /// </summary>
    public Task<int> ExitTask { get; }
}

/// <summary>
/// Stdin-side write surface of an <see cref="IHarnessSession"/>.
/// The pi 0.99.2 <c>--mode rpc</c> protocol
/// (<c>openspec/changes/add-orchestra/spike-1b-report.md</c>) is
/// line-delimited JSON; each command is one full JSON object
/// followed by LF and a <c>StreamWriter</c> flush. The harness's
/// <c>Capabilities.LiveSession = true</c> declaration
/// (<c>specs/harness-spi/spec.md</c> Requirement "IHarness is the
/// abstraction", scenario "pi declares LiveSession = true") is
/// what the worker checks before turning an inbound
/// <see cref="Shared.Contracts.Grpc.TurnInput"/> into a
/// write through this surface.
/// </summary>
public interface ITurnInputWriter
{
    /// <summary>
    /// <c>steer</c> lands a new user turn mid-flight; pi queues
    /// the message and delivers it on the next iteration of the
    /// agent loop (the <c>specs/session/spec.md</c> "Authoritative
    /// turn replaces accumulated text" scenario).
    /// </summary>
    /// <param name="turnId">JSON-RPC <c>id</c> the worker uses to correlate the response.</param>
    /// <param name="text">The user's turn body (the same text the operator's steer endpoint received).</param>
    /// <returns><c>false</c> when the stream is closed (process dead or session disposed).</returns>
    public bool TryWriteSteer(string turnId, string text);

    /// <summary>
    /// <c>follow_up</c> queues the message for the *next* turn —
    /// used when the inbound <c>TurnInput</c> arrives after
    /// <c>agent_settled</c> (the same-session branch in the
    /// worker-side dispatch — <c>WorkerCommandHandler</c> decides
    /// which command to use based on whether the session has
    /// settled; the harness itself is passive on the choice).
    /// </summary>
    /// <param name="turnId">JSON-RPC <c>id</c>.</param>
    /// <param name="text">The user's turn body.</param>
    /// <returns><c>false</c> when the stream is closed.</returns>
    public bool TryWriteFollowUp(string turnId, string text);
}

/// <summary>
/// Input to <see cref="IHarnessRuntime.StartSessionAsync"/>. One
/// per <see cref="Execution.Run.WorkerRun"/>. The brief is the initial user turn
/// the harness processes on its first <c>prompt</c>; <see cref="Environment"/>
/// and <see cref="WorkingDirectory"/> are pass-through to the
/// underlying <c>ProcessStartInfo</c> of the
/// <see cref="IHarnessRuntime"/> implementation.
/// </summary>
/// <param name="Brief">The initial turn body — the initial turn the harness processes on its first <c>prompt</c> command.</param>
/// <param name="Environment">Per-process env stamps (proxy base URL, virtual key, etc.). <c>null</c> leaves the harness's inherited environment untouched.</param>
/// <param name="WorkingDirectory">The harness's <c>cwd</c>. <c>null</c> falls back to <c>TranslatorOptions.WorkingDirectory</c>.</param>
public sealed record HarnessStartRequest(
    string Brief,
    IReadOnlyDictionary<string, string>? Environment,
    string? WorkingDirectory);
