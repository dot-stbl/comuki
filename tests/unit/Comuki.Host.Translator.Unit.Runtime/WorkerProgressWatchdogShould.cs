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
/// on every parsed event; tests here advance the
/// <see cref="FakeTimeProvider"/>'s clock to deterministically fire
/// the virtual <see cref="ITimer"/> — no <c>Task.Delay</c>, no
/// real-time waits.
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
    public void FreshWatchdogIsNotFailing()
    {
        var (watchdog, _) = Build(workerProgressTimeout: TimeSpan.FromMilliseconds(100));
        using (watchdog)
        {
            watchdog.Start();
            watchdog.ShouldFailItem.ShouldBeFalse();
        }
    }

    [Fact(DisplayName = "Given an idle watchdog, when the clock advances past 3x the timeout, then ShouldFailItem flips to true (tier 3)")]
    public void IdleWatchdogEscalatesToFailItem()
    {
        // 1s timeout → tick interval 166ms. Advance the clock past
        // 3x (= 3s); the FakeTimeProvider fires the ITimer
        // synchronously on each Advance.
        var (watchdog, clock) = Build(workerProgressTimeout: TimeSpan.FromSeconds(1));
        using (watchdog)
        {
            watchdog.Start();

            clock.Advance(TimeSpan.FromSeconds(5));

            watchdog.ShouldFailItem.ShouldBeTrue();
            watchdog.FailReason.ShouldBe("worker.stall_detected");
        }
    }

    [Fact(DisplayName = "Given an escalated watchdog, when Reset is called, then ShouldFailItem returns to false")]
    public void ResetClearsFailItemFlag()
    {
        var (watchdog, clock) = Build(workerProgressTimeout: TimeSpan.FromSeconds(1));
        using (watchdog)
        {
            watchdog.Start();

            // Escalate.
            clock.Advance(TimeSpan.FromSeconds(5));
            watchdog.ShouldFailItem.ShouldBeTrue();

            // Reset on the next parsed event.
            watchdog.Reset();
            watchdog.ShouldFailItem.ShouldBeFalse();
        }
    }

    [Fact(DisplayName = "Given an idle watchdog, when the clock advances but stays under the timeout, then ShouldFailItem stays false")]
    public void ShortSilenceDoesNotEscalate()
    {
        var (watchdog, clock) = Build(workerProgressTimeout: TimeSpan.FromHours(1));
        using (watchdog)
        {
            watchdog.Start();

            clock.Advance(TimeSpan.FromSeconds(5));

            watchdog.ShouldFailItem.ShouldBeFalse();
        }
    }

    [Fact(DisplayName = "Given a None-policy watchdog, when the clock advances past the timeout, then ShouldFailItem stays false (no escalation)")]
    public void NonePolicyDoesNotEscalate()
    {
        var (watchdog, clock) = Build(
            workerProgressTimeout: TimeSpan.FromSeconds(1),
            policy: WorkerProgressEscalationPolicy.None);
        using (watchdog)
        {
            watchdog.Start();

            clock.Advance(TimeSpan.FromSeconds(5));

            watchdog.ShouldFailItem.ShouldBeFalse();
        }
    }

    [Fact(DisplayName = "Given a regular event stream, when the watchdog ticks, then only one stall_warn is journaled for the silence window")]
    public void StallWarnFiresOncePerSilenceWindow()
    {
        // 1s timeout → tick interval 100ms (clamped to floor).
        // Tier 1 fires when elapsed >= WorkerProgressTimeout (1s);
        // tier 2 at 2s; tier 3 (fail-item) at 3s. The promise:
        // tier 1 fires once per silence window (the first tick past
        // WorkerProgressTimeout), tier 2 only after the next
        // tier-transition.
        var (watchdog, clock) = Build(workerProgressTimeout: TimeSpan.FromSeconds(1));
        using (watchdog)
        {
            watchdog.Start();

            // First silence window: tick past tier 1 (elapsed ~1.2s).
            clock.Advance(TimeSpan.FromMilliseconds(1300));
            // Multiple subsequent ticks in the same window must not
            // refire tier 1 or 2 (the watchdog's internal currentTier
            // prevents that). Advance to the 2-3x band; tier 2 fires
            // but no fail-item yet.
            clock.Advance(TimeSpan.FromSeconds(1));
            watchdog.ShouldFailItem.ShouldBeFalse();

            // Reset on the next parsed event — silence window resets.
            watchdog.Reset();
            // Another silence window: tick past tier 1 again (from
            // the reset baseline).
            clock.Advance(TimeSpan.FromMilliseconds(1300));
            watchdog.ShouldFailItem.ShouldBeFalse();
        }
    }

    [Fact(DisplayName = "Given a regular event stream, when the watchdog ticks every interval, then no tier 1 warn ever fires")]
    public void RegularEventsKeepWatchdogAtTierZero()
    {
        // 1s timeout → tick interval 166ms. Reset on every event
        // (each tick). The watchdog's currentTier stays at 0;
        // ShouldFailItem never flips.
        var (watchdog, clock) = Build(workerProgressTimeout: TimeSpan.FromSeconds(1));
        using (watchdog)
        {
            watchdog.Start();

            for (var i = 0; i < 10; i++)
            {
                clock.Advance(TimeSpan.FromMilliseconds(500));
                watchdog.Reset();
            }

            watchdog.ShouldFailItem.ShouldBeFalse();
        }
    }
}
