using System.Threading.Channels;
using Comuki.Host.Translator.Api.Models.Responses;
using Comuki.Host.Translator.Execution.Loop;
using Comuki.Host.Translator.Execution.Outcomes;
using Comuki.Host.Translator.Execution.Run;
using Comuki.Host.Translator.Parsing;
using Comuki.Host.Translator.Runtime;
using Comuki.Shared.Kernel.Harness;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Host.Translator.Unit.Runtime;

/// <summary>
/// Worker-runtime spec scenario "Non-zero pi exit fails the item" plus
/// the "outcome carries stderr alongside the exit code" addition:
/// when the harness streams its events cleanly (the events iterator
/// returns without exception) but the OS exit code is non-zero, the
/// pump's post-disposal check fails the item and carries both the
/// exit code and the captured stderr tail in
/// <see cref="PiOutcome.ErrorText"/>. The cancellation / invalid-op
/// paths preserve their own shape (the spec's "Failures propagate"
/// baseline for non-heartbeat components still applies; the
/// heartbeat-isolation carve-out is its own
/// <see cref="HeartbeatMonitorShould"/> test).
/// <para>
/// Why a custom session here rather than <see cref="TestFakeHarness"/>:
/// the production fake emits one wave per inbound command and keeps
/// the channel open across them — the existing TestFakeHarnessSessionShould
/// exercises the "multiple waves across one channel" semantic. For the
/// PiPump exit-code path we need a session whose channel completes
/// after one wave so the pump's <c>await foreach</c> exits naturally
/// (no cancellation, no exception) and the post-disposal ExitCode
/// branch can be exercised. <see cref="SingleWaveSession"/> is the
/// single-purpose seam that gives the pump a clean drain.
/// </para>
/// </summary>
public sealed class PiPumpShould
{
    [Fact(DisplayName = "Given a session that streams cleanly but exits 0, when PumpAsync runs, then the outcome is SuccessStatus and ErrorText is empty")]
    public async Task CleanExitReturnsSuccessAsync()
    {
        var harness = new SingleWaveHarness(new SingleWaveSession(exitCode: null, stderrTail: null));

        var outcome = await PumpAsync(harness);

        outcome.Status.ShouldBe(PiOutcome.SuccessStatus);
        outcome.ErrorText.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given a session that streams cleanly but exits non-zero with a stderr tail, when PumpAsync runs, then the outcome is failed carrying both the exit code and the stderr tail in ErrorText")]
    public async Task NonZeroExitAppendsStderrTailAsync()
    {
        const int exit = 3;
        const string stderrTail = "Error: model not found\n  at <anonymous> (handler.ts:42)";
        var harness = new SingleWaveHarness(new SingleWaveSession(exitCode: exit, stderrTail: stderrTail));

        var outcome = await PumpAsync(harness);

        outcome.Status.ShouldBe(PiOutcome.FailedStatus, "the events iterator completed; only the harness's own exit code flags the run as failed");
        outcome.ErrorText.ShouldContain($"harness exited with code {exit}");
        outcome.ErrorText.ShouldContain(stderrTail);
    }

    [Fact(DisplayName = "Given a non-zero exit with no stderr captured, when PumpAsync runs, then the outcome is failed carrying the exit code only (no spurious stderr label)")]
    public async Task NonZeroExitWithoutStderrCarriesExitCodeOnlyAsync()
    {
        const int exit = 7;
        var harness = new SingleWaveHarness(new SingleWaveSession(exitCode: exit, stderrTail: null));

        var outcome = await PumpAsync(harness);

        outcome.Status.ShouldBe(PiOutcome.FailedStatus);
        outcome.ErrorText.ShouldBe($"harness exited with code {exit}");
        outcome.ErrorText.ShouldNotContain("stderr:");
    }

    /// <summary>
    /// Drives <see cref="PiPump.PumpAsync"/> with the bare minimum
    /// surface it needs: a <see cref="WorkerRunSummary"/> for the
    /// event-fold, a fixed started-at instant, and the run's
    /// <see cref="CancellationToken"/>. The harness session is what
    /// carries the behaviour under test; the worker gRPC stream
    /// (<c>WorkerRun.Session</c>) is intentionally not wired —
    /// the only events <see cref="SingleWaveSession"/> emits are
    /// session lifecycle events that <see cref="PiEventToWorkerEvent.ToForwardEvent"/>
    /// maps to <c>null</c>, so the pump never calls
    /// <c>run.Session.SendAsync</c> on the unwired stub. The
    /// <see cref="WorkerRun"/> ctor takes a non-nullable
    /// worker session; we pass <c>null!</c> with a boundary-comment
    /// because the pump's path doesn't dereference it in these tests.
    /// </summary>
    private static async Task<PiOutcome> PumpAsync(IHarnessRuntime harness)
    {
        var runCancellation = new CancellationTokenSource();
        var workItemId = Guid.NewGuid();
        var claimed = new ClaimedWorkItemResponse(
            workItemId,
            RunId: Guid.NewGuid(),
            ProjectId: Guid.NewGuid(),
            ProfileKey: "test-profile",
            EnvClass: "net10-sdk-bun",
            Brief: "test-brief",
            LeaseUntilUnixMs: 0,
            Attempt: 1,
            Generation: 1);
        // boundary: WorkerRun.Session is non-nullable but the pump's
        // path never calls SendAsync on it in these tests (see class
        // remarks). The SingleWaveSession emits only lifecycle events
        // that PiEventToWorkerEvent maps to null.
        var run = new WorkerRun(claimed, null!)
        {
            RunCancellation = runCancellation, HarnessSession = Substitute.For<IHarnessSession>()
        };
        var summary = new WorkerRunSummary();
        var startedAt = DateTimeOffset.UtcNow;
        var clock = TimeProvider.System;
        // PiPump is a static class — use a forwarding logger factory
        // rather than `NullLogger<PiPump>` (static types aren't allowed
        // as generic arguments).
        var loggerFactory = NullLoggerFactory.Instance;

        return await PiPump.PumpAsync(
            harness,
            run,
            summary,
            startedAt,
            clock,
            loggerFactory.CreateLogger(nameof(PiPump)));
    }

    /// <summary>
    /// Test-only <see cref="IHarnessRuntime"/> that returns a
    /// pre-built <see cref="SingleWaveSession"/> on
    /// <see cref="StartSessionAsync"/>. Lets the pump consume the
    /// session under test from the same handle that built it (no
    /// second harness, no second session).
    /// </summary>
    private sealed class SingleWaveHarness(IHarnessSession session) : IHarnessRuntime
    {
        public string Name { get; } = "single-wave-pi-session-harness";

        public HarnessCapabilities Capabilities { get; } = new(liveSession: true);

        public Task<IHarnessSession> StartSessionAsync(HarnessStartRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(session);
        }
    }

    /// <summary>
    /// One-shot harness session: pre-fills a <see cref="Channel{T}"/> with
    /// one prompt wave (agent_start → agent_settled), then completes the
    /// channel before the constructor returns — the pump's
    /// <c>await foreach</c> is guaranteed to see all five events and exit
    /// naturally on the first <c>MoveNextAsync</c> past the end. Sync
    /// completion is the test seam: the production <c>TestFakeHarness</c>
    /// uses <c>Task.Run</c> because it has to model "the wave lands on
    /// stdin then pi replies asynchronously"; for the pump's outcome
    /// we don't need that — the test only cares about the post-foreach
    /// exit-code / stderr path. Intentionally omits
    /// <c>AssistantTextEvent</c> / <c>ToolCallEvent</c> / etc. — the only
    /// events that map to a forwardable worker event — because the
    /// test's <see cref="WorkerRun"/> carries a <c>null</c> worker
    /// session stub (the gRPC channel is irrelevant to the outcome-shape
    /// assertions; see the rationale on <see cref="PumpAsync"/> for
    /// the null).
    /// <see cref="ExitCode"/> and <see cref="StderrTail"/> are
    /// constructor-pinned — the test configures them up front and the
    /// pump reads them post-disposal to drive the non-zero-exit and
    /// stderr-appended outcome paths.
    /// </summary>
    private sealed class SingleWaveSession : IHarnessSession
    {
        private readonly Channel<PiEvent> events;

        public SingleWaveSession()
            : this(exitCode: null, stderrTail: null)
        {
        }

        public SingleWaveSession(int? exitCode, string? stderrTail)
        {
            ExitCode = exitCode;
            StderrTail = stderrTail;
            events = Channel.CreateUnbounded<PiEvent>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true,
            });
            events.Writer.TryWrite(new PiEvent.AgentStartEvent());
            events.Writer.TryWrite(new PiEvent.TurnStartEvent());
            events.Writer.TryWrite(new PiEvent.TurnEndEvent());
            events.Writer.TryWrite(new PiEvent.AgentEndEvent());
            events.Writer.TryWrite(new PiEvent.AgentSettledEvent());
            events.Writer.TryComplete();
        }

        public int ProcessId { get; } = -1;

        public int? ExitCode { get; }

        public string? StderrTail { get; }

        public IAsyncEnumerable<PiEvent> Events => events.Reader.ReadAllAsync();

        public ITurnInputWriter TurnInputs { get; } = new NullTurnInputWriter();

        public ValueTask DisposeAsync()
        {
            return default;
        }

        /// <summary>Null-object turn writer: this session never accepts turns (the pump is single-pass in these tests).</summary>
        private sealed class NullTurnInputWriter : ITurnInputWriter
        {
            public bool TryWriteSteer(string turnId, string text)
            {
                return false;
            }

            public bool TryWriteFollowUp(string turnId, string text)
            {
                return false;
            }

            public static void CloseStdin()
            {
            }
        }
    }
}
