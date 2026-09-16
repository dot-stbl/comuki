using Comuki.Modules.Memory.Application.Ports;
using Comuki.Modules.Memory.Infrastructure.Configuration;
using Comuki.Modules.Memory.Infrastructure.Persistence.Stores;
using Comuki.Shared.Bootstrap.Workers;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Memory.Unit;

/// <summary>
/// The consolidation worker's orchestration over a substituted memory
/// store: thresholds flow from <see cref="MemoryConsolidationOptions"/>
/// into the store calls, the cycle reports the promote/decay/total
/// counters on <see cref="WorkerResult.Data"/>, and a DB-layer failure
/// becomes a failing result (the registry backs off) instead of an
/// unhandled throw. The SQL-side candidate rules are covered by
/// <see cref="MemoryConsolidationRulesShould"/> and the Testcontainers
/// suite.
/// </summary>
public sealed class MemoryConsolidationComukiWorkerShould
{
    private static readonly DateTimeOffset now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given default options, when the cycle runs, then promote/decay are called with the default thresholds and the counters are reported")]
    public async Task RunPassesWithDefaultThresholdsAsync()
    {
        var fixtures = NewWorker(new MemoryConsolidationOptions());
        fixtures.Store.CountActiveFactsAsync(Arg.Any<CancellationToken>()).Returns(150);

        var result = await ExecuteOnceAsync(fixtures.Worker);

        result.Success.ShouldBeTrue();
        var counters = result.Data.ShouldBeOfType<MemoryConsolidationCounters>();
        counters.Promoted.ShouldBe(2);
        counters.Decayed.ShouldBe(0);
        counters.Total.ShouldBe(150);
        await fixtures.Store.Received(1).PromoteReadFactsAsync(
            now, 3, MemoryConsolidationComukiWorker.PromoteMinAge, Arg.Any<CancellationToken>());
        await fixtures.Store.Received(1).DecayUnreadFactsAsync(
            now, TimeSpan.FromDays(90), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given configured options, when the cycle runs, then the configured thresholds flow into the store calls")]
    public async Task RunPassesConfiguredThresholdsAsync()
    {
        var settings = new MemoryConsolidationOptions { PromoteReadThreshold = 5, DecayDays = 30 };
        var fixtures = NewWorker(settings);

        await ExecuteOnceAsync(fixtures.Worker);

        await fixtures.Store.Received(1).PromoteReadFactsAsync(
            now, 5, MemoryConsolidationComukiWorker.PromoteMinAge, Arg.Any<CancellationToken>());
        await fixtures.Store.Received(1).DecayUnreadFactsAsync(
            now, TimeSpan.FromDays(30), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given the default options, when the schedule is read, then the worker polls every six hours")]
    public void ScheduleDefaultsToSixHours()
    {
        var fixtures = NewWorker(new MemoryConsolidationOptions());

        var schedule = fixtures.Worker.Schedule.ShouldBeOfType<WorkerSchedule.IntervalWorkerSchedule>();

        schedule.PollInterval.ShouldBe(TimeSpan.FromHours(6));
        fixtures.Worker.Name.ShouldBe("memory-consolidation");
    }

    [Fact(DisplayName = "Given a database-layer failure on the promote pass, when the cycle runs, then the result fails instead of throwing")]
    public async Task ReportFailureOnDatabaseErrorAsync()
    {
        var fixtures = NewWorker(new MemoryConsolidationOptions());
        fixtures.Store.PromoteReadFactsAsync(
                Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TimeoutException("upstream timed out"));

        var result = await ExecuteOnceAsync(fixtures.Worker);

        result.Success.ShouldBeFalse();
        result.Detail.ShouldNotBeNull().ShouldContain("consolidation failed");
    }

    private static async Task<WorkerResult> ExecuteOnceAsync(MemoryConsolidationComukiWorker worker)
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        return await worker.ExecuteAsync(
            new WorkerContext(services, ConsolidationFixedTime.Provider, NullLogger.Instance),
            TestContext.Current.CancellationToken);
    }

    private static WorkerFixtures NewWorker(MemoryConsolidationOptions settings)
    {
        var store = Substitute.For<IMemoryStore>();
        store.PromoteReadFactsAsync(
                Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(2);
        store.DecayUnreadFactsAsync(
                Arg.Any<DateTimeOffset>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(0);
        store.CountActiveFactsAsync(Arg.Any<CancellationToken>()).Returns(150);

        var worker = new MemoryConsolidationComukiWorker(
            store,
            Options.Create(settings),
            Substitute.For<ISubjectScopeAccessor>(),
            ConsolidationFixedTime.Provider,
            NullLogger<MemoryConsolidationComukiWorker>.Instance);
        return new WorkerFixtures(store, worker);
    }

    private sealed record WorkerFixtures(IMemoryStore Store, MemoryConsolidationComukiWorker Worker);
}

/// <summary>Deterministic clock local to this file (file-static so it does not collide with the other fixtures).</summary>
file static class ConsolidationFixedTime
{
    public static readonly TimeProvider Provider = new FixedTimeProvider();

    private sealed class FixedTimeProvider : TimeProvider
    {
        private static readonly DateTimeOffset now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            return now;
        }
    }
}
