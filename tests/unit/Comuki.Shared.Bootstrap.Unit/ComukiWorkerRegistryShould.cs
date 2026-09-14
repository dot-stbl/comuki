using Comuki.Shared.Bootstrap.Workers;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Bootstrap.Unit;

/// <summary>
/// Registry contract: interval workers run their first cycle immediately
/// and reschedule with exponential backoff on failure (reset by a
/// success), startup workers run exactly once, thrown exceptions count
/// as failures without crashing the registry, and
/// <see cref="ComukiWorkerRegistry.Snapshot"/> reflects all of it.
/// Polling with deadlines — no real sleeps beyond tiny intervals.
/// </summary>
public sealed class ComukiWorkerRegistryShould
{
    [Fact(DisplayName = "Given an interval worker, when the registry starts, then the first cycle runs immediately and the snapshot is healthy")]
    public async Task RunFirstCycleImmediatelyAsync()
    {
        var worker = new CountingWorker("probe", WorkerSchedule.Interval(TimeSpan.FromMinutes(1)));
        await using var harness = await NewHarnessAsync(worker);

        var status = await WaitUntilStatusAsync(harness.Registry, "probe", static candidate => candidate.LastResult is not null);

        worker.Executions.ShouldBeGreaterThanOrEqualTo(1);
        status.LastRunAt.ShouldNotBeNull();
        status.LastResult!.Success.ShouldBeTrue();
        status.ConsecutiveFailures.ShouldBe(0);
        status.IsHealthy.ShouldBeTrue();
        // the next cycle is a full interval out, not immediate
        status.NextRunAt.ShouldNotBeNull();
    }

    [Fact(DisplayName = "Given a startup worker, when the registry starts, then it executes exactly once and is never rescheduled")]
    public async Task RunStartupWorkerOnceAsync()
    {
        var worker = new CountingWorker("boot", WorkerSchedule.Startup());
        await using var harness = await NewHarnessAsync(worker);

        var status = await WaitUntilStatusAsync(harness.Registry, "boot", static candidate => candidate.LastResult is not null);
        await Task.Delay(TimeSpan.FromMilliseconds(150), TestContext.Current.CancellationToken);

        worker.Executions.ShouldBe(1);
        status.NextRunAt.ShouldBeNull("a startup worker is never rescheduled");
    }

    [Fact(DisplayName = "Given a failing worker, when cycles fail, then consecutive failures grow, health flips false and the next run backs off by at least 2x the interval")]
    public async Task BackOffOnFailuresAsync()
    {
        var interval = TimeSpan.FromMilliseconds(40);
        var worker = new CountingWorker("flaky", WorkerSchedule.Interval(interval), static _ => WorkerResult.Fail("nope"));
        await using var harness = await NewHarnessAsync(worker);

        var status = await WaitUntilStatusAsync(harness.Registry, "flaky", static candidate => candidate.ConsecutiveFailures >= 1);

        worker.Executions.ShouldBeGreaterThanOrEqualTo(1);
        status.IsHealthy.ShouldBeFalse();
        status.LastResult!.Success.ShouldBeFalse();
        status.NextRunAt.ShouldNotBeNull();
        var backoff = status.NextRunAt!.Value - status.LastRunAt!.Value;
        backoff.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromTicks(interval.Ticks * 2), "first failure doubles the interval");
    }

    [Fact(DisplayName = "Given a worker that throws, when the cycle throws, then the exception is contained, counted as a failure and the loop keeps running")]
    public async Task ContainThrownExceptionsAsync()
    {
        var interval = TimeSpan.FromMilliseconds(30);
        var worker = new CountingWorker("thrower", WorkerSchedule.Interval(interval), static _ => throw new InvalidOperationException("boom"));
        await using var harness = await NewHarnessAsync(worker);

        var status = await WaitUntilStatusAsync(harness.Registry, "thrower", static candidate => candidate.ConsecutiveFailures >= 2);

        worker.Executions.ShouldBeGreaterThanOrEqualTo(2);
        status.IsHealthy.ShouldBeFalse();
        status.LastResult!.Detail.ShouldBe("InvalidOperationException");
    }

    [Fact(DisplayName = "Given a worker that fails then succeeds, when the next cycle succeeds, then the failure count resets and health recovers")]
    public async Task ResetFailuresOnSuccessAsync()
    {
        var interval = TimeSpan.FromMilliseconds(30);
        var worker = new CountingWorker(
            "recover",
            WorkerSchedule.Interval(interval),
            static execution => execution == 1 ? WorkerResult.Fail("once") : WorkerResult.Ok("fine"));
        await using var harness = await NewHarnessAsync(worker);

        var failed = await WaitUntilStatusAsync(harness.Registry, "recover", static candidate => candidate.ConsecutiveFailures >= 1);
        failed.IsHealthy.ShouldBeFalse();

        var recovered = await WaitUntilStatusAsync(harness.Registry, "recover", static candidate => candidate.LastResult is { Success: true });
        worker.Executions.ShouldBeGreaterThanOrEqualTo(2);
        recovered.ConsecutiveFailures.ShouldBe(0);
        recovered.IsHealthy.ShouldBeTrue();
    }

    [Fact(DisplayName = "Given no workers registered, when the registry starts, then it exits and the snapshot is empty")]
    public async Task HostZeroWorkersGracefullyAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddComukiWorkers();
        await using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<ComukiWorkerRegistry>();
        await registry.StartAsync(TestContext.Current.CancellationToken);

        registry.Snapshot().ShouldBeEmpty();
    }

    private static async Task<RegistryHarness> NewHarnessAsync(params IComukiWorker[] workers)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        foreach (var worker in workers)
        {
            services.AddSingleton(worker);
        }

        services.AddComukiWorkers();
        var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<ComukiWorkerRegistry>();
        await registry.StartAsync(TestContext.Current.CancellationToken);

        return new RegistryHarness(provider, registry);
    }

    private static async Task<WorkerStatus> WaitUntilStatusAsync(
        ComukiWorkerRegistry registry,
        string name,
        Func<WorkerStatus, bool> predicate)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var status = registry.Snapshot().FirstOrDefault(candidate => candidate.Name == name);
            if (status is not null && predicate(status))
            {
                return status;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), TestContext.Current.CancellationToken);
        }

        throw new TimeoutException($"worker '{name}' never reached the expected status");
    }

    /// <summary>Owns the provider and stops the registry on dispose, so every test cleans its loop up.</summary>
    private sealed record RegistryHarness(ServiceProvider Provider, ComukiWorkerRegistry Registry) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Registry.StopAsync(CancellationToken.None);
            await Provider.DisposeAsync();
        }
    }

    /// <summary>Manual fake: counts executions and derives each cycle's result from the execution number.</summary>
    /// <param name="name">Worker name.</param>
    /// <param name="schedule">Schedule to expose.</param>
    /// <param name="behave">Maps the 1-based execution number to a result; may throw.</param>
    private sealed class CountingWorker(string name, WorkerSchedule schedule, Func<int, WorkerResult>? behave = null) : IComukiWorker
    {
        private int executions;

        public string Name => name;

        public WorkerSchedule Schedule => schedule;

        public int Executions => Volatile.Read(ref executions);

        public Task<WorkerResult> ExecuteAsync(WorkerContext context, CancellationToken cancellationToken)
        {
            var execution = Interlocked.Increment(ref executions);
            return Task.FromResult(behave?.Invoke(execution) ?? WorkerResult.Ok($"cycle {execution}"));
        }
    }
}
