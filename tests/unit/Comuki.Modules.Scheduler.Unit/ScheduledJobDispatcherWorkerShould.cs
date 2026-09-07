using System.Globalization;
using Comuki.Modules.Scheduler.Application.Observers;
using Comuki.Modules.Scheduler.Application.Options;
using Comuki.Modules.Scheduler.Application.Ports;
using Comuki.Modules.Scheduler.Domain.Ids;
using Comuki.Modules.Scheduler.Domain.Jobs;
using Comuki.Modules.Scheduler.Infrastructure.Sync;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Scheduler.Unit;

/// <summary>
/// ScheduledJobDispatcherWorker polling cycle: due jobs are dispatched,
/// the fire trail stamps <c>last_fired_at</c> + the next fire instant,
/// one failing job does not poison the rest of the batch, and every
/// registered observer is notified of each successful fire.
/// </summary>
public sealed class ScheduledJobDispatcherWorkerShould
{
    private static readonly DateTimeOffset anchorTime = DateTimeOffset.Parse(
        "2026-09-06T00:00:00Z",
        CultureInfo.InvariantCulture);

    [Fact(DisplayName = "Given a due enabled job, when the worker polls once, then the dispatcher is called and the fire trail is stamped")]
    public async Task DispatchDueJobAsync()
    {
        var clock = new FixedClock(anchorTime);
        var due = ScheduledJob.Create(
            new ProjectId(Guid.CreateVersion7()),
            "*/5 * * * *",
            "general",
            "{}",
            runOnOnceAt: clock.GetUtcNow().AddMinutes(-1),
            enabled: true,
            clock.GetUtcNow());
        var initialNextFire = due.NextFireAt;

        var dispatcher = Substitute.For<ISchedulerDispatcher>();
        dispatcher.DispatchAsync(due, Arg.Any<CancellationToken>())
            .Returns(new RunId(Guid.CreateVersion7()));

        var observer = Substitute.For<ISchedulerObserver>();
        var worker = NewWorker(clock, [due], dispatcher, [observer]);

        var fired = await worker.PollOnceAsync(TestContext.Current.CancellationToken);

        fired.ShouldBe(1);
        await dispatcher.Received(1).DispatchAsync(
            Arg.Is<ScheduledJob>(job => job.Id == due.Id),
            Arg.Any<CancellationToken>());
        due.LastFiredAt.ShouldNotBeNull();
        due.NextFireAt.ShouldNotBe(initialNextFire);
    }

    [Fact(DisplayName = "Given two due jobs, when the worker polls once, then both are dispatched in the same batch")]
    public async Task DispatchesMultipleJobsInOneCycleAsync()
    {
        var clock = new FixedClock(anchorTime);
        var projectId = new ProjectId(Guid.CreateVersion7());
        var first = ScheduledJob.Create(projectId, "*/5 * * * *", "general", "{}", clock.GetUtcNow().AddMinutes(-2), true, clock.GetUtcNow());
        var second = ScheduledJob.Create(projectId, "0 9 * * *", "general", "{}", clock.GetUtcNow().AddMinutes(-1), true, clock.GetUtcNow());

        var dispatcher = Substitute.For<ISchedulerDispatcher>();
        dispatcher.DispatchAsync(Arg.Any<ScheduledJob>(), Arg.Any<CancellationToken>())
            .Returns(new RunId(Guid.CreateVersion7()));

        var worker = NewWorker(clock, [first, second], dispatcher, observers: []);

        var fired = await worker.PollOnceAsync(TestContext.Current.CancellationToken);

        fired.ShouldBe(2);
        await dispatcher.Received(2).DispatchAsync(Arg.Any<ScheduledJob>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a dispatch that throws, when the worker polls, then the other jobs still fire and the failing job stays due")]
    public async Task PerJobIsolationAsync()
    {
        var clock = new FixedClock(anchorTime);
        var projectId = new ProjectId(Guid.CreateVersion7());
        var failing = ScheduledJob.Create(projectId, "*/5 * * * *", "general", "{}", clock.GetUtcNow().AddMinutes(-2), true, clock.GetUtcNow());
        var succeeding = ScheduledJob.Create(projectId, "0 9 * * *", "general", "{}", clock.GetUtcNow().AddMinutes(-1), true, clock.GetUtcNow());

        var dispatcher = Substitute.For<ISchedulerDispatcher>();
        dispatcher.DispatchAsync(
                Arg.Is<ScheduledJob>(job => job.Id == failing.Id),
                Arg.Any<CancellationToken>())
            .Returns<Task<RunId>>(_ => throw new HttpRequestException("upstream gone"));
        dispatcher.DispatchAsync(
                Arg.Is<ScheduledJob>(job => job.Id == succeeding.Id),
                Arg.Any<CancellationToken>())
            .Returns(new RunId(Guid.CreateVersion7()));

        var worker = NewWorker(clock, [failing, succeeding], dispatcher, observers: []);

        var fired = await worker.PollOnceAsync(TestContext.Current.CancellationToken);

        // boundary: the worker's own per-job isolation — one failing
        // dispatch schedules a retry on the next cycle (the job stays
        // due) and never stops the rest of the batch.
        fired.ShouldBe(1);
        await dispatcher.Received(1).DispatchAsync(
            Arg.Is<ScheduledJob>(job => job.Id == failing.Id),
            Arg.Any<CancellationToken>());
        await dispatcher.Received(1).DispatchAsync(
            Arg.Is<ScheduledJob>(job => job.Id == succeeding.Id),
            Arg.Any<CancellationToken>());
        succeeding.LastFiredAt.ShouldNotBeNull();
        failing.LastFiredAt.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a registered observer, when the worker fires a job, then the observer is notified with the fire metadata")]
    public async Task ObserversReceiveFiredEventAsync()
    {
        var clock = new FixedClock(anchorTime);
        var firedAt = clock.GetUtcNow();
        var projectId = new ProjectId(Guid.CreateVersion7());
        var due = ScheduledJob.Create(
            projectId,
            "*/5 * * * *",
            "ops-sentry",
            "{}",
            firedAt,
            enabled: true,
            clock.GetUtcNow());

        var dispatcher = Substitute.For<ISchedulerDispatcher>();
        dispatcher.DispatchAsync(due, Arg.Any<CancellationToken>())
            .Returns(new RunId(Guid.CreateVersion7()));

        var observer = Substitute.For<ISchedulerObserver>();
        var worker = NewWorker(clock, [due], dispatcher, [observer]);

        await worker.PollOnceAsync(TestContext.Current.CancellationToken);

        await observer.Received(1).OnJobFiredAsync(
            Arg.Is<ScheduledJobId>(id => id.Value == due.Id.Value),
            Arg.Is<ProjectId>(id => id.Value == projectId.Value),
            Arg.Is("ops-sentry"),
            Arg.Any<RunId>(),
            Arg.Is<DateTimeOffset>(value => value == firedAt),
            Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given an observer that throws, when the worker fires, then the fire still counts and the remaining observers still run")]
    public async Task FailingObserverDoesNotPoisonTheBatchAsync()
    {
        var clock = new FixedClock(anchorTime);
        var firedAt = clock.GetUtcNow();
        var due = ScheduledJob.Create(
            new ProjectId(Guid.CreateVersion7()),
            "*/5 * * * *",
            "general",
            "{}",
            firedAt,
            enabled: true,
            clock.GetUtcNow());

        var dispatcher = Substitute.For<ISchedulerDispatcher>();
        dispatcher.DispatchAsync(due, Arg.Any<CancellationToken>())
            .Returns(new RunId(Guid.CreateVersion7()));

        var failing = Substitute.For<ISchedulerObserver>();
        failing.OnJobFiredAsync(
                Arg.Any<ScheduledJobId>(),
                Arg.Any<ProjectId>(),
                Arg.Any<string>(),
                Arg.Any<RunId>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(static _ => throw new HttpRequestException("observer down"));

        var succeeding = Substitute.For<ISchedulerObserver>();
        var worker = NewWorker(clock, [due], dispatcher, [failing, succeeding]);

        var fired = await worker.PollOnceAsync(TestContext.Current.CancellationToken);

        fired.ShouldBe(1);
        await failing.Received(1).OnJobFiredAsync(
            Arg.Any<ScheduledJobId>(),
            Arg.Any<ProjectId>(),
            Arg.Any<string>(),
            Arg.Any<RunId>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
        await succeeding.Received(1).OnJobFiredAsync(
            Arg.Any<ScheduledJobId>(),
            Arg.Any<ProjectId>(),
            Arg.Any<string>(),
            Arg.Any<RunId>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    private static ScheduledJobDispatcherWorker NewWorker(
        TimeProvider clock,
        IReadOnlyList<ScheduledJob> dueJobs,
        ISchedulerDispatcher dispatcher,
        IReadOnlyList<ISchedulerObserver> observers)
    {
        var store = Substitute.For<IScheduledJobStore>();
        store.ListDueAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(dueJobs);

        var services = new ServiceCollection();
        services.AddScoped(_ => store);
        services.AddScoped(_ => dispatcher);

        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var scopeAccessor = Substitute.For<ISubjectScopeAccessor>();
        scopeAccessor.AsSystem(Arg.Any<string>()).Returns(new SystemScope());

        return new ScheduledJobDispatcherWorker(
            scopeFactory,
            scopeAccessor,
            clock,
            Options.Create(new SchedulerOptions { PollInterval = TimeSpan.FromSeconds(30) }),
            observers,
            NullLogger<ScheduledJobDispatcherWorker>.Instance);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return now;
        }
    }

    private sealed class SystemScope : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
