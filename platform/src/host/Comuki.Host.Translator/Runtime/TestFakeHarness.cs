using System.Threading.Channels;
using Comuki.Host.Translator.Parsing;
using Comuki.Shared.Kernel.Harness;

namespace Comuki.Host.Translator.Runtime;

/// <summary>
/// In-process <see cref="IHarnessRuntime"/> for unit and
/// integration tests (add-orchestra Phase 1c — <c>specs/worker-runtime/spec.md</c>
/// Requirement "Harness abstraction is the Translator public surface",
/// scenario "TestFakeHarness is the second implementation").
/// <para>
/// Each <see cref="StartSessionAsync"/> call returns a
/// <see cref="FakeHarnessSession"/> that echoes the JSON-RPC
/// command shape (the same shape
/// <c>openspec/changes/add-orchestra/spike-1b-report.md</c>
/// documents for <c>pi --mode rpc</c>) and synthesises
/// <c>agent_start</c> / <c>turn_start</c> /
/// <c>message_update</c> / <c>message_end</c> / <c>turn_end</c> /
/// <c>agent_settled</c> events in response. <c>prompt</c> and
/// the inbound <c>steer</c> / <c>follow_up</c> commands all
/// produce one echo wave each; the fake is the test seam that
/// proves the worker side of the bidi channel (the harness's
/// <see cref="ITurnInputWriter"/>) actually delivers the
/// operator's turn into the model — without an actual
/// <c>pi</c> process in the test loop.
/// </para>
/// <para>
/// The test fake is a single class that implements the full
/// runtime half of the harness SPI: the existing capability
/// (<c>Capabilities.LiveSession</c>) plus <see cref="StartSessionAsync"/>
/// returning a <see cref="FakeHarnessSession"/>. The
/// <c>Capabilities.LiveSession</c> flag is configurable so the same
/// harness stands in for the <c>LiveSession = true</c> unit-test
/// path (the live session bid) and the
/// <c>LiveSession = false</c> path (the no-session follow-up).
/// </para>
/// </summary>
/// <param name="liveSession">The capability the fake declares. The production <see cref="PiHarness"/> is always <c>true</c>; the test surface is the value the unit test pins.</param>
public sealed class TestFakeHarness(bool liveSession) : IHarnessRuntime
{
    /// <inheritdoc />
    public string Name => HarnessIds.TestFakePi;

    /// <inheritdoc />
    public HarnessCapabilities Capabilities { get; } = new(liveSession: liveSession);

    /// <inheritdoc />
    public Task<IHarnessSession> StartSessionAsync(
        HarnessStartRequest request,
        CancellationToken cancellationToken = default)
    {
        var session = new FakeHarnessSession(liveSession, request.Brief, cancellationToken);
        return Task.FromResult<IHarnessSession>(session);
    }
}

/// <summary>
/// One in-process harness session. Owns the events channel
/// (one synthetic event wave per inbound <c>prompt</c> /
/// <c>steer</c> / <c>follow_up</c> command) and the turn-input
/// writer (synchronous — the fake's "process" is in-memory).
/// <para>
/// Wire format parity with <c>pi --mode rpc</c>: each inbound
/// command is a JSON object with <c>type</c> / <c>id</c> /
/// <c>message</c> fields; the response is one full event wave
/// beginning with <c>agent_start</c> and ending with
/// <c>agent_settled</c>. The fake is the test seam that proves
/// the worker side of the bidi channel (the harness's
/// <see cref="ITurnInputWriter"/>) actually delivers the
/// operator's turn into the model — without an actual
/// <c>pi</c> process in the test loop.
/// </para>
/// </summary>
internal sealed class FakeHarnessSession : IHarnessSession
{
    private const int SyntheticProcessId = -1;
    private const string InitialResponseText = "PONG";
    private readonly Channel<PiEvent> events;
    private readonly CancellationTokenSource disposedCts = new();
    private readonly FakeTurnInputWriter writer;
    private readonly bool liveSession;
    private readonly TaskCompletionSource<int> exitSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool disposed;

    public FakeHarnessSession(bool liveSession, string brief, CancellationToken cancellationToken)
    {
        this.liveSession = liveSession;
        ProcessId = SyntheticProcessId;
        events = Channel.CreateUnbounded<PiEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });
        writer = new FakeTurnInputWriter(this);

        // Run the initial wave in the background — the worker
        // (or the unit test) starts iterating Events right after
        // StartSessionAsync returns.
        _ = Task.Run(
            () => EmitWaveAsync(turnId: "p-initial", promptText: brief, responseText: InitialResponseText, liveSessionSteer: true, cancellationToken),
            cancellationToken);
    }

    public int ProcessId { get; }

    public IAsyncEnumerable<PiEvent> Events => events.Reader.ReadAllAsync(disposedCts.Token);

    public ITurnInputWriter TurnInputs => writer;

    public Task<int> ExitTask => exitSource.Task;

    /// <summary>
    /// Records one inbound turn (steer / follow_up) on the fake
    /// session and emits one echo event wave in response. The
    /// writer (<see cref="FakeTurnInputWriter"/>) is the only
    /// caller — the lock-free path is the per-session state
    /// machine the worker drives.
    /// </summary>
    /// <param name="turnId">The JSON-RPC <c>id</c> the writer minted.</param>
    /// <param name="text">The inbound turn text.</param>
    /// <param name="responseText">The synthesised assistant response the fake echoes back.</param>
    /// <param name="liveSessionSteer"><c>true</c> when the inbound command is a steer (mid-flight, lands on the live session); <c>false</c> when it's a follow_up (post- <c>agent_settled</c>, queued for the next turn).</param>
    internal void RecordTurnAndEmitWave(string turnId, string text, string responseText, bool liveSessionSteer)
    {
        if (disposed)
        {
            return;
        }

        _ = Task.Run(() => EmitWaveAsync(turnId, text, responseText, liveSessionSteer, disposedCts.Token));
    }

    private async Task EmitWaveAsync(
        string turnId,
        string promptText,
        string responseText,
        bool liveSessionSteer = true,
        CancellationToken cancellationToken = default)
    {
        // The wave shape mirrors the documented pi --mode rpc events
        // (per spike-1b-report.md §"Mechanism in detail"): a
        // response frame lands on stdout first (the harness's
        // synchronous command ack), then the event wave.
        // StreamJsonParser already routes JSON objects without a
        // known type to UnknownEvent; for the test fake the
        // intermediate response frame is harmless. (A future
        // refactor could teach the parser about the response
        // type, but it's not load-bearing for the unit surface.)

        if (!liveSession)
        {
            // The LiveSession=false path is the no-session branch
            // of the worker — turn input is dropped at the worker
            // boundary. The fake still records the turn on the
            // writer's log so tests can assert the worker did
            // dispatch (the existing SteerNoLiveSessionStagesFollowUp
            // asserts no follow-up via the no-LiveSession worker
            // path; the harness itself stays passive).
            return;
        }

        await WriteEventAsync(new PiEvent.AgentStartEvent(), cancellationToken);
        await WriteEventAsync(new PiEvent.TurnStartEvent(), cancellationToken);
        await WriteEventAsync(new PiEvent.TextDeltaEvent(ContentIndex: 0, Delta: responseText), cancellationToken);
        await WriteEventAsync(new PiEvent.AssistantTextEvent(Text: responseText), cancellationToken);
        await WriteEventAsync(new PiEvent.TurnEndEvent(), cancellationToken);
        await WriteEventAsync(new PiEvent.AgentEndEvent(), cancellationToken);
        await WriteEventAsync(new PiEvent.AgentSettledEvent(), cancellationToken);
    }

    private async Task WriteEventAsync(PiEvent piEvent, CancellationToken cancellationToken)
    {
        if (disposed)
        {
            return;
        }

        try
        {
            await events.Writer.WriteAsync(piEvent, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // expected on session dispose
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        disposedCts.Cancel();
        events.Writer.TryComplete();
        exitSource.TrySetResult(0);
        writer.CloseStdin();
        await Task.CompletedTask;
    }
}

/// <summary>
/// The fake's stdin-side writer. Forwards <c>steer</c> /
/// <c>follow_up</c> commands back to the session so the session
/// can emit the echo event wave. Stays synchronous — there is no
/// real pipe to flush, just an in-memory method call.
/// </summary>
internal sealed class FakeTurnInputWriter(FakeHarnessSession session) : ITurnInputWriter
{
    private static readonly string responseText = "PONG2";

    private readonly FakeHarnessSession session = session;
    private bool closed;
    private long turnCounter;

    public bool TryWriteSteer(string turnId, string text)
    {
        if (closed)
        {
            return false;
        }

        var id = $"{turnId}-{Interlocked.Increment(ref turnCounter)}";
        session.RecordTurnAndEmitWave(id, text, responseText, liveSessionSteer: true);
        return true;
    }

    public bool TryWriteFollowUp(string turnId, string text)
    {
        if (closed)
        {
            return false;
        }

        var id = $"{turnId}-{Interlocked.Increment(ref turnCounter)}";
        session.RecordTurnAndEmitWave(id, text, responseText, liveSessionSteer: false);
        return true;
    }

    public void CloseStdin()
    {
        closed = true;
    }
}
