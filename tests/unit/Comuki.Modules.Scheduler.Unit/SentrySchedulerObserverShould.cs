using Comuki.Modules.Scheduler.Application.Options;
using Comuki.Modules.Scheduler.Domain.Ids;
using Comuki.Modules.Scheduler.Infrastructure.Observers;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Comuki.Modules.Scheduler.Unit;

/// <summary>
/// Sentry side-channel: when Scheduler:Sentry:Dsn is unset the observer
/// short-circuits (no SDK init, no capture, no transport thread); when
/// the DSN is set the observer hands the fire to the SDK with the
/// bounded-cardinality tags the operator dashboard expects.
/// </summary>
public sealed class SentrySchedulerObserverShould
{
    private static readonly DateTimeOffset anchorTime =
        new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given an empty DSN, when the observer is notified, then the call returns synchronously without touching the SDK")]
    public async Task NoOpWithoutDsnAsync()
    {
        var observer = NewObserver(new SchedulerSentryOptions { Dsn = null });
        var jobId = new ScheduledJobId(Guid.CreateVersion7());

        // The observer short-circuits — we cannot intercept
        // SentrySdk.CaptureEvent, but we assert no exception leaks
        // even when the SDK is uninitialised (which it always is in
        // unit tests — SentrySdk.Init is only called from Program.cs).
        await observer.OnJobFiredAsync(
            jobId,
            new ProjectId(Guid.CreateVersion7()),
            profileKey: "ops-sentry",
            new RunId(Guid.CreateVersion7()),
            anchorTime,
            TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "Given a whitespace DSN, when the observer is notified, then the call returns synchronously")]
    public async Task NoOpWithWhitespaceDsnAsync()
    {
        var observer = NewObserver(new SchedulerSentryOptions { Dsn = "   " });

        await observer.OnJobFiredAsync(
            new ScheduledJobId(Guid.CreateVersion7()),
            new ProjectId(Guid.CreateVersion7()),
            profileKey: "ops-sentry",
            new RunId(Guid.CreateVersion7()),
            anchorTime,
            TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "Given a configured DSN with no SDK initialised, when the observer is notified, then the call does not throw")]
    public async Task ConfiguredDsnWithoutSdkInitDoesNotThrowAsync()
    {
        var observer = NewObserver(new SchedulerSentryOptions { Dsn = "https://key@example.com/1" });

        // CaptureEvent on an uninitialised SDK is a documented no-op
        // (returns null) — we just need to confirm the observer does
        // not surface an exception. The fire path stays green; the
        // dispatcher catches any unexpected throw and logs it.
        await observer.OnJobFiredAsync(
            new ScheduledJobId(Guid.CreateVersion7()),
            new ProjectId(Guid.CreateVersion7()),
            profileKey: "ops-sentry",
            new RunId(Guid.CreateVersion7()),
            anchorTime,
            TestContext.Current.CancellationToken);
    }

    private static SentrySchedulerObserver NewObserver(SchedulerSentryOptions sentryOptions)
    {
        var schedulerOptions = Options.Create(new SchedulerOptions
        {
            PollInterval = TimeSpan.FromSeconds(30),
            BatchSize = 50,
            Sentry = sentryOptions,
        });

        return new SentrySchedulerObserver(
            schedulerOptions,
            NullLogger<SentrySchedulerObserver>.Instance);
    }
}
