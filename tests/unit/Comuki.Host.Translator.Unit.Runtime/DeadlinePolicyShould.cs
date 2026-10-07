using Comuki.Host.Translator.Execution.Run;
using Comuki.Host.Translator.Runtime;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Comuki.Host.Translator.Unit.Runtime;

/// <summary>
/// Lifecycle of <see cref="DeadlinePolicy"/>: two wall-clock
/// budgets (turn + run) with the same escalation chain as the
/// progress watchdog. Turn-budget breaches are tier-2 gentle-kill
/// for the first
/// <see cref="TranslatorOptions.ConsecutiveTurnBreachesBeforeFail"/>
/// cycles, then fail-item; run-budget breach is one-shot fail-item.
/// <para>
/// Test timing note: the policy's <see cref="ITimer"/> is created
/// via <see cref="TimeProvider.CreateTimer"/>, and the
/// <see cref="FakeTimeProvider"/> fires the timer synchronously on
/// each <see cref="FakeTimeProvider.Advance"/>. With a 12s
/// TurnBudget the tick interval is 1s; one
/// <c>Advance(12.5s)</c> fires the timer once past the budget
/// (counter=1), one <c>Advance(14s)</c> fires it ~3 times.
/// </para>
/// <para>
/// The turn-budget chain is "inside the same worker process" — the
/// counter lives on <see cref="DeadlineChainState"/> which the
/// <c>TranslatorLoop</c> creates once and passes to every
/// <c>DeadlinePolicy</c> instance. The integration-style test at
/// the bottom proves the chain accumulates across policy
/// recreations.
/// </para>
/// </summary>
public sealed class DeadlinePolicyShould
{
    private static readonly Guid workItemId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static (DeadlinePolicy Policy, FakeTimeProvider Clock, DeadlineChainState State) Build(
        WorkerRun run,
        FakeTimeProvider clock,
        TimeSpan? turnBudget = null,
        TimeSpan? runBudget = null,
        int? consecutiveTurnBreachesBeforeFail = null)
    {
        var options = Options.Create(WorkerSessionTestHelpers.NewOptions(
            turnBudget: turnBudget,
            runBudget: runBudget,
            consecutiveTurnBreachesBeforeFail: consecutiveTurnBreachesBeforeFail));
        var state = new DeadlineChainState(consecutiveTurnBreachesBeforeFail ?? 3);
        var policy = new DeadlinePolicy(run, state, options, clock, NullLogger<DeadlinePolicy>.Instance);
        return (policy, clock, state);
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

    [Fact(DisplayName = "Given a fresh policy, when the clock stays under both budgets, then ShouldFailItem is false")]
    public void FreshPolicyIsNotFailing()
    {
        var clock = new FakeTimeProvider();
        var run = NewRun(clock);
        var (policy, _, _) = Build(
            run,
            clock,
            turnBudget: TimeSpan.FromMinutes(5),
            runBudget: TimeSpan.FromHours(1));
        using (policy)
        {
            policy.Start();

            clock.Advance(TimeSpan.FromSeconds(30));

            policy.ShouldFailItem.ShouldBeFalse();
        }
    }

    [Fact(DisplayName = "Given a run-budget breach, when the clock advances past RunBudget, then ShouldFailItem is true on the first tick (one-shot)")]
    public void RunBudgetBreachFailsItemImmediately()
    {
        var clock = new FakeTimeProvider();
        var run = NewRun(clock);
        var (policy, _, _) = Build(
            run,
            clock,
            turnBudget: TimeSpan.FromMinutes(5),
            runBudget: TimeSpan.FromSeconds(1));
        using (policy)
        {
            policy.Start();

            clock.Advance(TimeSpan.FromSeconds(5));

            policy.ShouldFailItem.ShouldBeTrue();
            policy.FailReason.ShouldBe("worker.run_budget_exceeded");
        }
    }

    [Fact(DisplayName = "Given one turn-budget breach tick, when only that tick fires, then ShouldFailItem is false (gentle-kill, no fail yet)")]
    public void SingleTurnBreachIsGentleKill()
    {
        // 12s TurnBudget → tick interval 1s. Advance(12.5s) fires
        // exactly one tick past the budget (at 12s); the next tick
        // at 13s is past the Advance's target. Threshold is 3, so
        // the policy does not fail-item.
        var clock = new FakeTimeProvider();
        var run = NewRun(clock);
        var (policy, _, state) = Build(
            run,
            clock,
            turnBudget: TimeSpan.FromSeconds(12),
            runBudget: TimeSpan.FromHours(1),
            consecutiveTurnBreachesBeforeFail: 3);
        using (policy)
        {
            policy.Start();

            clock.Advance(TimeSpan.FromSeconds(12.5));

            policy.ShouldFailItem.ShouldBeFalse();
            state.ConsecutiveBreaches.ShouldBe(1);
        }
    }

    [Fact(DisplayName = "Given three turn-budget breach ticks on one cycle, then ShouldFailItem is true and FailReason is worker.turn_budget_exceeded")]
    public void TurnBudgetChainReachesFailItemOnSingleCycle()
    {
        // 12s TurnBudget → tick interval 1s. Advance(14s) fires
        // ~14 ticks; the 3rd trips the threshold.
        var clock = new FakeTimeProvider();
        var run = NewRun(clock);
        var (policy, _, state) = Build(
            run,
            clock,
            turnBudget: TimeSpan.FromSeconds(12),
            runBudget: TimeSpan.FromHours(1),
            consecutiveTurnBreachesBeforeFail: 3);
        using (policy)
        {
            policy.Start();

            clock.Advance(TimeSpan.FromSeconds(14));

            policy.ShouldFailItem.ShouldBeTrue();
            policy.FailReason.ShouldBe("worker.turn_budget_exceeded");
            state.ConsecutiveBreaches.ShouldBeGreaterThanOrEqualTo(3);
        }
    }

    [Fact(DisplayName = "Given three cycles on the same DeadlineChainState (1 breach per cycle, threshold 3), then the third cycle's policy flips ShouldFailItem (counter survives recreations)")]
    public void TurnBudgetChainSurvivesPolicyRecreations()
    {
        // 12s TurnBudget → tick interval 1s. Each cycle: one
        // advance of 12.5s fires exactly one tick past the budget
        // (at 12s). The state lives across policy recreations.
        //
        // Production note: each cycle creates a fresh WorkerRun
        // with a new RunStartedAt = clock.GetUtcNow() at cycle
        // start. In this test the WorkerRun is shared (the counter
        // on the state is the point), so we mutate RunStartedAt
        // on the run between cycles to mirror the production reset.
        var clock = new FakeTimeProvider();
        var run = NewRun(clock);
        var sharedState = new DeadlineChainState(consecutiveTurnBreachesBeforeFail: 3);

        // Cycle 1: 1 breach. Counter = 1.
        var (policy1, _, _) = BuildWithState(
            run, sharedState, clock,
            turnBudget: TimeSpan.FromSeconds(12),
            runBudget: TimeSpan.FromHours(1));
        using (policy1)
        {
            policy1.Start();
            clock.Advance(TimeSpan.FromSeconds(12.5));
            policy1.ShouldFailItem.ShouldBeFalse();
            sharedState.ConsecutiveBreaches.ShouldBe(1);
        }

        // Cycle 2: 2nd breach. Counter survives policy recreation.
        // Production: new WorkerRun, new RunStartedAt.
        run.GetType().GetProperty("RunStartedAt")!.SetValue(run, clock.GetUtcNow());
        var (policy2, _, _) = BuildWithState(
            run, sharedState, clock,
            turnBudget: TimeSpan.FromSeconds(12),
            runBudget: TimeSpan.FromHours(1));
        using (policy2)
        {
            policy2.Start();
            clock.Advance(TimeSpan.FromSeconds(12.5));
            policy2.ShouldFailItem.ShouldBeFalse();
            sharedState.ConsecutiveBreaches.ShouldBe(2);
        }

        // Cycle 3: 3rd breach. Threshold tripped, fail-item.
        run.GetType().GetProperty("RunStartedAt")!.SetValue(run, clock.GetUtcNow());
        var (policy3, _, _) = BuildWithState(
            run, sharedState, clock,
            turnBudget: TimeSpan.FromSeconds(12),
            runBudget: TimeSpan.FromHours(1));
        using (policy3)
        {
            policy3.Start();
            clock.Advance(TimeSpan.FromSeconds(12.5));

            policy3.ShouldFailItem.ShouldBeTrue();
            policy3.FailReason.ShouldBe("worker.turn_budget_exceeded");
            sharedState.ConsecutiveBreaches.ShouldBe(3);
        }
    }

    [Fact(DisplayName = "Given a counter on DeadlineChainState, when ResetBreachCounter is called, then the next cycle starts at zero")]
    public void ResetBreachCounterClearsAcrossCycles()
    {
        var clock = new FakeTimeProvider();
        var run = NewRun(clock);
        var sharedState = new DeadlineChainState(consecutiveTurnBreachesBeforeFail: 3);

        var (policy1, _, _) = BuildWithState(
            run, sharedState, clock,
            turnBudget: TimeSpan.FromSeconds(12),
            runBudget: TimeSpan.FromHours(1));
        using (policy1)
        {
            policy1.Start();
            clock.Advance(TimeSpan.FromSeconds(12.5));
            sharedState.ConsecutiveBreaches.ShouldBe(1);
            policy1.ResetBreachCounter();
            sharedState.ConsecutiveBreaches.ShouldBe(0);
        }

        // Next cycle starts fresh — 1 breach is gentle-kill.
        // Production: new WorkerRun, new RunStartedAt.
        run.GetType().GetProperty("RunStartedAt")!.SetValue(run, clock.GetUtcNow());
        var (policy2, _, _) = BuildWithState(
            run, sharedState, clock,
            turnBudget: TimeSpan.FromSeconds(12),
            runBudget: TimeSpan.FromHours(1));
        using (policy2)
        {
            policy2.Start();
            clock.Advance(TimeSpan.FromSeconds(12.5));
            policy2.ShouldFailItem.ShouldBeFalse();
            sharedState.ConsecutiveBreaches.ShouldBe(1);
        }
    }

    private static (DeadlinePolicy Policy, FakeTimeProvider Clock, DeadlineChainState State) BuildWithState(
        WorkerRun run,
        DeadlineChainState state,
        FakeTimeProvider clock,
        TimeSpan? turnBudget = null,
        TimeSpan? runBudget = null)
    {
        var options = Options.Create(WorkerSessionTestHelpers.NewOptions(
            turnBudget: turnBudget,
            runBudget: runBudget,
            consecutiveTurnBreachesBeforeFail: state.ConsecutiveTurnBreachesBeforeFail));
        var policy = new DeadlinePolicy(run, state, options, clock, NullLogger<DeadlinePolicy>.Instance);
        return (policy, clock, state);
    }
}
