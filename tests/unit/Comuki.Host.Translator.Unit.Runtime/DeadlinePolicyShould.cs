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
/// Test timing note: same as
/// <see cref="WorkerProgressWatchdogShould"/> — the policy's
/// internal <see cref="Timer"/> fires on real
/// time (1s lower clamp). Timeouts stay above the clamp to keep
/// the tick responsive.
/// </para>
/// </summary>
public sealed class DeadlinePolicyShould
{
    private static readonly Guid workItemId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static (DeadlinePolicy Policy, FakeTimeProvider Clock) Build(
        TimeSpan? turnBudget = null,
        TimeSpan? runBudget = null,
        int? consecutiveTurnBreachesBeforeFail = null)
    {
        var clock = new FakeTimeProvider();
        var options = Options.Create(WorkerSessionTestHelpers.NewOptions(
            turnBudget: turnBudget,
            runBudget: runBudget,
            consecutiveTurnBreachesBeforeFail: consecutiveTurnBreachesBeforeFail));
        var run = WorkerSessionTestHelpers.NewRun(
            NSubstitute.Substitute.For<Shared.Contracts.Grpc.IWorkerService>(),
            workItemId,
            new CancellationTokenSource(),
            runStartedAt: clock.GetUtcNow(),
            processStartedAt: clock.GetUtcNow());
        var policy = new DeadlinePolicy(run, options, clock, NullLogger<DeadlinePolicy>.Instance);
        return (policy, clock);
    }

    [Fact(DisplayName = "Given a fresh policy, when the clock stays under both budgets, then ShouldFailItem is false")]
    public async Task FreshPolicyIsNotFailingAsync()
    {
        var (policy, clock) = Build(
            turnBudget: TimeSpan.FromMinutes(5),
            runBudget: TimeSpan.FromHours(1));
        using (policy)
        {
            policy.Start();

            clock.Advance(TimeSpan.FromSeconds(30));
            await Task.Delay(500, TestContext.Current.CancellationToken);

            policy.ShouldFailItem.ShouldBeFalse();
        }
    }

    [Fact(DisplayName = "Given a run-budget breach, when the clock advances past RunBudget, then ShouldFailItem is true on the first tick (one-shot)")]
    public async Task RunBudgetBreachFailsItemImmediatelyAsync()
    {
        var (policy, clock) = Build(
            turnBudget: TimeSpan.FromMinutes(5),
            runBudget: TimeSpan.FromSeconds(1));
        using (policy)
        {
            policy.Start();

            clock.Advance(TimeSpan.FromSeconds(5));
            await Task.Delay(500, TestContext.Current.CancellationToken);

            policy.ShouldFailItem.ShouldBeTrue();
        }
    }

    [Fact(DisplayName = "Given a turn-budget chain that reaches the threshold, then ShouldFailItem is true")]
    public async Task TurnBudgetChainReachesFailItemAsync()
    {
        // Use 1s budgets (tick interval 83ms). Advance enough
        // to accumulate 3 consecutive breaches; the policy
        // counts each tick where turnElapsed >= TurnBudget.
        var (policy, clock) = Build(
            turnBudget: TimeSpan.FromSeconds(1),
            runBudget: TimeSpan.FromHours(1),
            consecutiveTurnBreachesBeforeFail: 3);
        using (policy)
        {
            policy.Start();

            // Three separate advances, each followed by a
            // long-enough wait for the timer to tick, so the
            // policy observes three distinct consecutive turn-budget
            // breaches.
            for (var i = 0; i < 3; i++)
            {
                clock.Advance(TimeSpan.FromSeconds(5));
                await Task.Delay(500, TestContext.Current.CancellationToken);
            }

            policy.ShouldFailItem.ShouldBeTrue();
        }
    }
}
