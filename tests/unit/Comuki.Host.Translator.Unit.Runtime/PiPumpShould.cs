using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Comuki.Host.Translator.Api.Models.Responses;
using Comuki.Host.Translator.Execution.Loop;
using Comuki.Host.Translator.Execution.Outcomes;
using Comuki.Host.Translator.Execution.Run;
using Comuki.Host.Translator.Parsing;
using Comuki.Host.Translator.Runtime;
using Comuki.Shared.Kernel.Harness;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Host.Translator.Unit.Runtime;

/// <summary>
/// Integration tests for <see cref="PiPump.PumpAsync"/>: the pump
/// drives the <see cref="WorkerProgressWatchdog"/> and
/// <see cref="DeadlinePolicy"/> and folds their state into the
/// returned <see cref="PiOutcome"/>. These tests pin the typed
/// <see cref="PiOutcome.ErrorText"/> for the two escalation paths
/// that surface as <c>PiOutcome.FailedStatus</c>:
/// <c>worker.stall_detected</c> (progress watchdog tier 3) and
/// <c>worker.turn_budget_exceeded</c> (deadline-policy chain at
/// threshold). The <c>worker.run_budget_exceeded</c> path is
/// already covered by <c>DeadlinePolicyShould</c> and is
/// structurally the same as the turn-budget chain — same fail-item
/// signal, same outcome shape.
/// <para>
/// The pump reads the watchdogs' <c>ShouldFailItem</c> /
/// <c>FailReason</c> on every event iteration. A "silence past
/// budget" path therefore needs at least one event after the
/// watchdog fires to make the pump read the signal and exit. The
/// controllable fake harness below emits no events automatically;
/// the test emits exactly one event after advancing the clock to
/// the failure threshold, so the pump sees the fail-item signal
/// and returns <c>FailedStatus</c> with the typed reason.
/// </para>
/// <para>
/// Exit-code harness check (worker-runtime spec scenario "Non-zero
/// pi exit fails the item" plus the "outcome carries stderr
/// alongside the exit code" addition): when the harness streams
/// its events cleanly (the events iterator returns without
/// exception) but the OS exit code is non-zero, the pump's
/// post-disposal check fails the item and carries both the exit
/// code and the captured stderr tail in
/// <see cref="PiOutcome.ErrorText"/>. The cancellation /
/// invalid-op paths preserve their own shape (the spec's
/// "Failures propagate" baseline for non-heartbeat components
/// still applies; the heartbeat-isolation carve-out is its own
/// <see cref="HeartbeatMonitorShould"/> test).
/// </para>
/// <para>
/// Why a custom session here rather than
/// <see cref="TestFakeHarness"/>: the production fake emits one
/// wave per inbound command and keeps the channel open across
/// them. For the exit-code path we need a session whose channel
/// completes after one wave so the pump's <c>await foreach</c>
/// exits naturally (no cancellation, no exception) and the
/// post-disposal ExitCode branch can be exercised. The
/// <see cref="SingleWaveHarness"/> / <see cref="SingleWaveSession"/>
/// pair below is the single-purpose seam that gives the pump a
/// clean drain.
/// </para>
/// </summary>
public sealed class PiPumpShould
{
    private static readonly Guid workItemId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact(DisplayName = "Given an idle pump with a 1s WorkerProgressTimeout, when the clock advances past 3x the timeout, then the pump returns ErrorText worker.stall_detected")]
    public async Task SilenceEscalatesToStallDetectedAsync()
    {
        var clock = new FakeTimeProvider();
        var options = Options.Create(WorkerSessionTestHelpers.NewOptions(
            workerProgressTimeout: TimeSpan.FromSeconds(1),
            policy: WorkerProgressEscalationPolicy.WarnGentleKillFailItem));
        var run = NewRunForWatchdog(clock);
        var harness = new ControllableFakeHarness();

        // Start the pump on a background task — it parks awaiting
        // events from the controllable session's channel. We
        // wait for the park signal, advance the clock past 3x the
        // timeout (tier 3 fail-item fires on the watchdog, which
        // cancels the run's CancellationToken; the pump's catch
        // path reads the watchdog's FailReason and surfaces it as
        // the outcome's ErrorText).
        var pumpTask = PiPump.PumpAsync(
            harness, run,
            new DeadlineChainState(consecutiveTurnBreachesBeforeFail: 3),
            new WorkerRunSummary(),
            startedAt: clock.GetUtcNow(),
            clock: clock,
            options: options,
            logger: NullLogger.Instance,
            loggerFactory: NullLoggerFactory.Instance);

        await harness.LastSession!.ConsumerParked.Task.WaitAsync(TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(5));

        var outcome = await pumpTask;
        outcome.ErrorText.ShouldBe("worker.stall_detected");
    }

    [Fact(DisplayName = "Given a pump with a 12s TurnBudget and 3-breach threshold, when the clock advances past 3x the TurnBudget, then the pump returns ErrorText worker.turn_budget_exceeded")]
    public async Task TurnBudgetChainEscalatesToFailItemAsync()
    {
        // 12s TurnBudget → tick interval 1s. Advance(40s) fires
        // ~40 ticks past the budget; the third trip of the
        // threshold sets ShouldFailItem + FailReason =
        // "worker.turn_budget_exceeded" on the policy. The policy
        // cancels the run's CancellationToken; the pump's catch
        // path reads the policy's FailReason and surfaces it as the
        // outcome's ErrorText.
        var clock = new FakeTimeProvider();
        var options = Options.Create(WorkerSessionTestHelpers.NewOptions(
            turnBudget: TimeSpan.FromSeconds(12),
            runBudget: TimeSpan.FromHours(1),
            consecutiveTurnBreachesBeforeFail: 3));
        var run = NewRunForWatchdog(clock);
        var harness = new ControllableFakeHarness();

        var pumpTask = PiPump.PumpAsync(
            harness, run,
            new DeadlineChainState(consecutiveTurnBreachesBeforeFail: 3),
            new WorkerRunSummary(),
            startedAt: clock.GetUtcNow(),
            clock: clock,
            options: options,
            logger: NullLogger.Instance,
            loggerFactory: NullLoggerFactory.Instance);

        await harness.LastSession!.ConsumerParked.Task.WaitAsync(TestContext.Current.CancellationToken);
        // 40s — enough to fire 3+ ticks past the 12s TurnBudget
        // (tick interval 1s).
        clock.Advance(TimeSpan.FromSeconds(40));

        var outcome = await pumpTask;
        outcome.ErrorText.ShouldBe("worker.turn_budget_exceeded");
    }

    [Fact(DisplayName = "Given a session that streams cleanly but exits 0, when PumpAsync runs, then the outcome is SuccessStatus and ErrorText is empty")]
    public async Task CleanExitReturnsSuccessAsync()
    {
        var harness = new SingleWaveHarness(new SingleWaveSession(exitCode: null, stderrTail: null));

        var outcome = await PumpAsyncWithExitCodeCheckAsync(harness);

        outcome.Status.ShouldBe(PiOutcome.SuccessStatus);
        outcome.ErrorText.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given a session that streams cleanly but exits non-zero with a stderr tail, when PumpAsync runs, then the outcome is failed carrying both the exit code and the stderr tail in ErrorText")]
    public async Task NonZeroExitAppendsStderrTailAsync()
    {
        const int exit = 3;
        const string stderrTail = "Error: model not found\n  at <anonymous> (handler.ts:42)";
        var harness = new SingleWaveHarness(new SingleWaveSession(exitCode: exit, stderrTail: stderrTail));

        var outcome = await PumpAsyncWithExitCodeCheckAsync(harness);

        outcome.Status.ShouldBe(PiOutcome.FailedStatus, "the events iterator completed; only the harness's own exit code flags the run as failed");
        outcome.ErrorText.ShouldContain($"harness exited with code {exit}");
        outcome.ErrorText.ShouldContain(stderrTail);
    }

    [Fact(DisplayName = "Given a non-zero exit with no stderr captured, when PumpAsync runs, then the outcome is failed carrying the exit code only (no spurious stderr label)")]
    public async Task NonZeroExitWithoutStderrCarriesExitCodeOnlyAsync()
    {
        const int exit = 7;
        var harness = new SingleWaveHarness(new SingleWaveSession(exitCode: exit, stderrTail: null));

        var outcome = await PumpAsyncWithExitCodeCheckAsync(harness);

        outcome.Status.ShouldBe(PiOutcome.FailedStatus);
        outcome.ErrorText.ShouldBe($"harness exited with code {exit}");
        outcome.ErrorText.ShouldNotContain("stderr:");
    }

    private static WorkerRun NewRunForWatchdog(FakeTimeProvider clock)
    {
        return WorkerSessionTestHelpers.NewRun(
            Substitute.For<Shared.Contracts.Grpc.IWorkerService>(),
            workItemId,
            new CancellationTokenSource(),
            runStartedAt: clock.GetUtcNow(),
            processStartedAt: clock.GetUtcNow());
    }

    /// <summary>
    /// Drives <see cref="PiPump.PumpAsync"/> with the bare minimum
    /// surface the exit-code tests need: a <see cref="WorkerRunSummary"/>
    /// for the event-fold, a fixed started-at instant, the run's
    /// <see cref="CancellationToken"/>, default watchdogs/deadline
    /// thresholds (the exit-code check fires before any watchdog has
    /// time to do anything on a single-wave session), and a
    /// <see cref="TimeProvider.System"/> for the duration math. The
    /// harness session is what carries the behaviour under test; the
    /// worker gRPC stream (<c>WorkerRun.Session</c>) is intentionally
    /// not wired — the only events <see cref="SingleWaveSession"/>
    /// emits are session lifecycle events that
    /// <see cref="PiEventToWorkerEvent.ToForwardEvent"/> maps to
    /// <c>null</c>, so the pump never calls
    /// <c>run.Session.SendAsync</c> on the unwired stub. The
    /// <see cref="WorkerRun"/> ctor takes a non-nullable worker
    /// session; we pass <c>null!</c> with a boundary-comment because
    /// the pump's path doesn't dereference it in these tests.
    /// </summary>
    private static async Task<PiOutcome> PumpAsyncWithExitCodeCheckAsync(IHarnessRuntime harness)
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
        // that PiEventToWorkerEvent maps to null. RunStartedAt /
        // ProcessStartedAt are required on WorkerRun (harden-worker-runtime
        // Phase 1, watchdog/deadline inputs); the exit-code tests don't
        // exercise the watchdogs, so the timestamps stay at the default
        // minimum — the watchdogs never tick on a single-wave session
        // because the channel completes before the first deadline tick.
        var run = new WorkerRun(claimed, null!)
        {
            RunCancellation = runCancellation,
            HarnessSession = Substitute.For<IHarnessSession>(),
            RunStartedAt = DateTimeOffset.MinValue,
            ProcessStartedAt = DateTimeOffset.MinValue,
        };
        var summary = new WorkerRunSummary();
        var startedAt = DateTimeOffset.UtcNow;
        var clock = TimeProvider.System;
        var options = Options.Create(WorkerSessionTestHelpers.NewOptions());
        // PiPump is a static class — use a forwarding logger factory
        // rather than `NullLogger<PiPump>` (static types aren't allowed
        // as generic arguments).
        var loggerFactory = NullLoggerFactory.Instance;

        return await PiPump.PumpAsync(
            harness,
            run,
            new DeadlineChainState(consecutiveTurnBreachesBeforeFail: 3),
            summary,
            startedAt,
            clock,
            options,
            loggerFactory.CreateLogger(nameof(PiPump)),
            loggerFactory);
    }
}

/// <summary>
/// In-process <see cref="IHarnessRuntime"/> that lets the test
/// control exactly when events flow. <see cref="StartSessionAsync"/>
/// returns a <see cref="ControllableFakeHarnessSession"/> whose
/// <c>Events</c> iterator signals
/// <see cref="ControllableFakeHarnessSession.ConsumerParked"/> the
/// first time the pump begins iterating — that is the moment the
/// pump is parked awaiting events and a <c>clock.Advance()</c>
/// call fires the watchdogs' timers synchronously.
/// </summary>
internal sealed class ControllableFakeHarness : IHarnessRuntime
{
    public string Name => "controllable-fake-harness";

    public HarnessCapabilities Capabilities { get; } = new(liveSession: true);

    public ControllableFakeHarnessSession? LastSession { get; private set; }

    public Task<IHarnessSession> StartSessionAsync(
        HarnessStartRequest request,
        CancellationToken cancellationToken = default)
    {
        var session = new ControllableFakeHarnessSession();
        LastSession = session;
        return Task.FromResult<IHarnessSession>(session);
    }
}

/// <summary>
/// One harness session that emits no events automatically.
/// <see cref="EmitEvent"/> pushes one event into the channel; the
/// pump consumes it on the next foreach iteration. The
/// <see cref="ConsumerParked"/> TCS is the test's "the pump is
/// awaiting events" signal.
/// </summary>
internal sealed class ControllableFakeHarnessSession : IHarnessSession
{
    private readonly Channel<PiEvent> events = Channel.CreateUnbounded<PiEvent>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });

    public ControllableFakeHarnessSession()
    {
        ConsumerParked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TurnInputs = new ControllableNullTurnInputWriter();
    }

    public int ProcessId => -100;

    /// <summary>
    /// No OS process to inspect — the controllable harness runs
    /// in-process. <c>null</c> matches the production
    /// <c>FakeHarnessSession</c> contract: the pump's exit-code
    /// check sees <c>null</c> and skips the post-disposal branch,
    /// so the watchdog / deadline tests stay focused on their own
    /// failure paths.
    /// </summary>
    public int? ExitCode => null;

    /// <summary>No OS stderr to capture — in-process harness.</summary>
    public string? StderrTail => null;

    public IAsyncEnumerable<PiEvent> Events => AwaitedAsync();

    public ITurnInputWriter TurnInputs { get; }

    /// <summary>Signalled the first time the pump's foreach iterates the events channel.</summary>
    public TaskCompletionSource ConsumerParked { get; }

    public void EmitEvent(PiEvent piEvent)
    {
        events.Writer.TryWrite(piEvent);
    }

    public void Complete()
    {
        events.Writer.TryComplete();
    }

    public ValueTask DisposeAsync()
    {
        events.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    private async IAsyncEnumerable<PiEvent> AwaitedAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Signal before we hand the consumer the channel — the test
        // can now advance the clock knowing the pump is parked on
        // the next foreach iteration. TrySetResult is idempotent
        // across multiple MoveNextAsync invocations.
        ConsumerParked.TrySetResult();
        await foreach (var piEvent in events.Reader.ReadAllAsync(cancellationToken))
        {
            yield return piEvent;
        }
    }
}

/// <summary>Stub stdin-side writer for the controllable harness; never invoked in the pump tests.</summary>
file sealed class ControllableNullTurnInputWriter : ITurnInputWriter
{
    public bool TryWriteSteer(string turnId, string text)
    {
        return true;
    }

    public bool TryWriteFollowUp(string turnId, string text)
    {
        return true;
    }
}

/// <summary>
/// Test-only <see cref="IHarnessRuntime"/> that returns a
/// pre-built <see cref="SingleWaveSession"/> on
/// <see cref="StartSessionAsync"/>. Lets the pump consume the
/// session under test from the same handle that built it (no
/// second harness, no second session).
/// </summary>
internal sealed class SingleWaveHarness(IHarnessSession session) : IHarnessRuntime
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
/// assertions; see the rationale on
/// <see cref="PiPumpShould.PumpAsyncWithExitCodeCheckAsync"/> for the null).
/// <see cref="ExitCode"/> and <see cref="StderrTail"/> are
/// constructor-pinned — the test configures them up front and the
/// pump reads them post-disposal to drive the non-zero-exit and
/// stderr-appended outcome paths.
/// </summary>
internal sealed class SingleWaveSession : IHarnessSession
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

    public ITurnInputWriter TurnInputs { get; } = new SingleWaveNullTurnInputWriter();

    public ValueTask DisposeAsync()
    {
        return default;
    }

    /// <summary>Null-object turn writer: this session never accepts turns (the pump is single-pass in these tests).</summary>
    private sealed class SingleWaveNullTurnInputWriter : ITurnInputWriter
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
