using Comuki.Host.Translator.Runtime;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Comuki.Host.Translator.Unit.Runtime;

/// <summary>
/// Lifecycle of <see cref="WorkerProgressWatchdog"/>: tracks
/// <c>last_event_age</c>, escalates through three tiers when
/// <see cref="TranslatorOptions.WorkerProgressTimeout"/> is exceeded
/// (tier 1 warn → tier 2 gentle-kill → tier 3 fail-item). The
/// pump drives the watchdog via <see cref="WorkerProgressWatchdog.Reset"/>
/// on every parsed event; tests here drive the time clock to
/// deterministically advance through the escalation path.
/// <para>
/// Test timing note: the watchdog's internal
/// <see cref="Timer"/> fires on a real-time cadence
/// (<c>WorkerProgressTimeout / 6</c>, clamped to 1–30s). Tests
/// therefore use timeouts that keep the timer interval at or below
/// the 1s lower clamp and wait long enough for the timer to tick
/// at least once after the <see cref="FakeTimeProvider"/> has
/// advanced past the tier-3 boundary.
/// </para>
/// </summary>
public sealed class WorkerProgressWatchdogShould
{
    private static readonly Guid workItemId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static (WorkerProgressWatchdog Watchdog, FakeTimeProvider Clock) Build(
        TimeSpan? workerProgressTimeout = null,
        WorkerProgressEscalationPolicy? policy = null)
    {
        var clock = new FakeTimeProvider();
        var options = Options.Create(WorkerSessionTestHelpers.NewOptions(
            workerProgressTimeout: workerProgressTimeout,
            policy: policy));
        var run = WorkerSessionTestHelpers.NewRun(
            NSubstitute.Substitute.For<Shared.Contracts.Grpc.IWorkerService>(),
            workItemId,
            new CancellationTokenSource(),
            runStartedAt: clock.GetUtcNow(),
            processStartedAt: clock.GetUtcNow());
        var watchdog = new WorkerProgressWatchdog(
            run, options, clock, NullLogger<WorkerProgressWatchdog>.Instance);
        return (watchdog, clock);
    }

    [Fact(DisplayName = "Given a fresh watchdog, when started, then ShouldFailItem is false")]
    public async Task FreshWatchdogIsNotFailingAsync()
    {
        var (watchdog, _) = Build(workerProgressTimeout: TimeSpan.FromMilliseconds(100));
        using (watchdog)
        {
            watchdog.Start();
            watchdog.ShouldFailItem.ShouldBeFalse();
            await Task.CompletedTask;
        }
    }

    [Fact(DisplayName = "Given an idle watchdog, when the clock advances past 3x the timeout, then ShouldFailItem flips to true (tier 3)")]
    public async Task IdleWatchdogEscalatesToFailItemAsync()
    {
        // 1s timeout → tick interval 166ms (no clamp). Advance the
        // clock past 3x (= 3s) and wait long enough for the timer
        // to tick.
        var (watchdog, clock) = Build(workerProgressTimeout: TimeSpan.FromSeconds(1));
        using (watchdog)
        {
            watchdog.Start();

            clock.Advance(TimeSpan.FromSeconds(5));
            // Wait for the timer to fire at least once.
            await Task.Delay(500, TestContext.Current.CancellationToken);

            watchdog.ShouldFailItem.ShouldBeTrue();
        }
    }

    [Fact(DisplayName = "Given an escalated watchdog, when Reset is called, then ShouldFailItem returns to false")]
    public async Task ResetClearsFailItemFlagAsync()
    {
        var (watchdog, clock) = Build(workerProgressTimeout: TimeSpan.FromSeconds(1));
        using (watchdog)
        {
            watchdog.Start();

            // Escalate.
            clock.Advance(TimeSpan.FromSeconds(5));
            await Task.Delay(500, TestContext.Current.CancellationToken);
            watchdog.ShouldFailItem.ShouldBeTrue();

            // Reset on the next parsed event.
            watchdog.Reset();
            watchdog.ShouldFailItem.ShouldBeFalse();
        }
    }

    [Fact(DisplayName = "Given an idle watchdog, when the clock advances but stays under the timeout, then ShouldFailItem stays false")]
    public async Task ShortSilenceDoesNotEscalateAsync()
    {
        var (watchdog, clock) = Build(workerProgressTimeout: TimeSpan.FromHours(1));
        using (watchdog)
        {
            watchdog.Start();

            clock.Advance(TimeSpan.FromSeconds(5));
            await Task.Delay(500, TestContext.Current.CancellationToken);

            watchdog.ShouldFailItem.ShouldBeFalse();
        }
    }

    [Fact(DisplayName = "Given a None-policy watchdog, when the clock advances past the timeout, then ShouldFailItem stays false (no escalation)")]
    public async Task NonePolicyDoesNotEscalateAsync()
    {
        var (watchdog, clock) = Build(
            workerProgressTimeout: TimeSpan.FromSeconds(1),
            policy: WorkerProgressEscalationPolicy.None);
        using (watchdog)
        {
            watchdog.Start();

            clock.Advance(TimeSpan.FromSeconds(5));
            await Task.Delay(500, TestContext.Current.CancellationToken);

            watchdog.ShouldFailItem.ShouldBeFalse();
        }
    }
}
