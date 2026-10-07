using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Comuki.Host.Translator.Execution.Loop;
using Comuki.Host.Translator.Execution.Outcomes;
using Comuki.Host.Translator.Execution.Run;
using Comuki.Host.Translator.Parsing;
using Comuki.Host.Translator.Runtime;
using Comuki.Shared.Kernel.Harness;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
        var run = NewRun(clock);
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
        var run = NewRun(clock);
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

    private static WorkerRun NewRun(FakeTimeProvider clock)
    {
        return WorkerSessionTestHelpers.NewRun(
            NSubstitute.Substitute.For<Shared.Contracts.Grpc.IWorkerService>(),
            workItemId,
            new CancellationTokenSource(),
            runStartedAt: clock.GetUtcNow(),
            processStartedAt: clock.GetUtcNow());
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
        TurnInputs = new NullTurnInputWriter();
    }

    public int ProcessId => -100;

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
file sealed class NullTurnInputWriter : ITurnInputWriter
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
