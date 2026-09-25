using Comuki.Shared.Bootstrap.Workers;
using Comuki.Shared.Editions.Edition;
using Comuki.Shared.Editions.Gating;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Bootstrap.Unit;

/// <summary>
/// Edition gating on <see cref="ComukiWorkerRegistry"/>: workers whose
/// implementation carries <see cref="RequiresFeatureAttribute"/> are
/// excluded when the current <see cref="IEdition"/> does not cover the
/// named feature (a Warning is logged at boot naming the worker and the
/// missing feature key), and the supervisor re-checks coverage on
/// <see cref="ComukiWorkerRegistry.DeferredRecheckInterval"/> so a
/// license upgrade starts them without restart. The Community-skip /
/// paid-include tests on a <c>background-llm-watchers</c> gated fake
/// worker are the worked example for the 6.2 task — no production
/// worker is tagged because every existing worker implements
/// Community-baseline functionality.
/// </summary>
public sealed class ComukiWorkerRegistryGatingShould
{
    private const string BackgroundLlmWatchersKey = "background-llm-watchers";

    [Fact(DisplayName = "Given a gated worker and an IEdition that does NOT cover its feature, when the registry starts, then the worker is excluded from the snapshot and a warning names the worker and the missing feature key")]
    public async Task GatedWorkerSkippedWhenFeatureNotCoveredAsync()
    {
        var edition = Substitute.For<IEdition>();
        edition.Has(Arg.Any<Editions.Catalog.Feature>()).Returns(false);
        var captured = new CapturingLoggerProvider();
        var worker = new GatedWorker("llm-watcher");

        await using var harness = await NewHarnessAsync(worker, edition, captured);

        harness.Registry.Snapshot().ShouldBeEmpty("a non-covered gated worker is excluded by WorkerFeatureGate");
        worker.Executions.ShouldBe(0);
        captured.ContainsWarningMatching(static entry =>
            entry.Message.Contains("llm-watcher", StringComparison.Ordinal)
            && entry.Message.Contains(BackgroundLlmWatchersKey, StringComparison.Ordinal))
            .ShouldBeTrue("the registry must log a Warning naming the worker and the missing feature key");
    }

    [Fact(DisplayName = "Given a gated worker and an IEdition that DOES cover its feature, when the registry starts, then the worker is in the snapshot and executes its first cycle")]
    public async Task GatedWorkerIncludedWhenFeatureCoveredAsync()
    {
        var edition = Substitute.For<IEdition>();
        edition.Has(Arg.Any<Editions.Catalog.Feature>()).Returns(true);

        var worker = new GatedWorker("llm-watcher");
        await using var harness = await NewHarnessAsync(worker, edition, captured: null);

        var status = await WaitUntilStatusAsync(harness.Registry, "llm-watcher", static candidate => candidate.LastResult is not null);

        worker.Executions.ShouldBeGreaterThanOrEqualTo(1);
        status.LastResult!.Success.ShouldBeTrue();
    }

    [Fact(DisplayName = "Given an ungated worker and no IEdition registered, when the registry starts, then the worker runs as it does today (no edition consultation)")]
    public async Task UngatedWorkerRunsWithoutEditionAsync()
    {
        var worker = new CountingWorker("no-attr");
        await using var harness = await NewHarnessAsync(worker, edition: null, captured: null);

        var status = await WaitUntilStatusAsync(harness.Registry, "no-attr", static candidate => candidate.LastResult is not null);

        worker.Executions.ShouldBeGreaterThanOrEqualTo(1);
        status.LastResult!.Success.ShouldBeTrue();
    }

    [Fact(DisplayName = "Given a gated worker and no IEdition registered, when the registry is resolved, then it throws InvalidOperationException naming the worker (wiring gap, not a soft skip)")]
    public Task GatedWorkerWithoutEditionThrowsAsync()
    {
        var worker = new GatedWorker("llm-watcher");
        var services = new ServiceCollection();
        services.AddLogging();

        // Registered AS IComukiWorker: the registry consumes
        // IEnumerable<IComukiWorker>, and AddSingleton(worker) with the
        // concrete compile-time type would be invisible to it.
        services.AddSingleton<IComukiWorker>(worker);
        services.AddComukiWorkers();

        using var provider = services.BuildServiceProvider();

        var exception = Should.Throw<InvalidOperationException>(
            provider.GetRequiredService<ComukiWorkerRegistry>);
        exception.Message.ShouldContain("llm-watcher");
        exception.Message.ShouldContain(BackgroundLlmWatchersKey);
        return Task.CompletedTask;
    }

    [Fact(DisplayName = "Given a deferred gated worker, when the edition flips to covering the feature, then the supervisor promotes the worker and the worker executes within the recheck window")]
    public async Task DeferredGatedWorkerPromotedOnEditionUpgradeAsync()
    {
        // Mutable fake — IsDegraded is irrelevant here, we toggle Has().
        var edition = Substitute.For<IEdition>();
        edition.Has(Arg.Any<Editions.Catalog.Feature>()).Returns(false);
        var worker = new GatedWorker("llm-watcher");

        var captured = new CapturingLoggerProvider();
        await using var harness = await NewHarnessAsync(
            worker,
            edition,
            captured,
            static registry => registry.DeferredRecheckInterval = TimeSpan.FromMilliseconds(50));

        // Confirm the skip branch fired at boot before we flip coverage.
        harness.Registry.Snapshot().ShouldBeEmpty();

        edition.Has(Arg.Any<Editions.Catalog.Feature>()).Returns(true);

        // Wait for the supervisor to observe the new coverage and start
        // the loop. Deadline-based poll — no Task.Delay-without-polling.
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline && worker.Executions == 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), TestContext.Current.CancellationToken);
        }

        worker.Executions.ShouldBeGreaterThanOrEqualTo(1, "the supervisor must promote and start the worker after coverage flips");
        harness.Registry.Snapshot().ShouldHaveSingleItem();
        captured.ContainsInformationMatching(static entry =>
            entry.Message.Contains("llm-watcher", StringComparison.Ordinal)
            && entry.Message.Contains(BackgroundLlmWatchersKey, StringComparison.Ordinal)
            && entry.Message.Contains("now covered", StringComparison.Ordinal))
            .ShouldBeTrue("the registry must log an Information line naming the worker + feature when it is promoted");
    }

    private static async Task<RegistryHarness> NewHarnessAsync(
        IComukiWorker worker,
        IEdition? edition,
        CapturingLoggerProvider? captured,
        Action<ComukiWorkerRegistry>? configure = null)
    {
        var services = new ServiceCollection();
        if (captured is not null)
        {
            services.AddLogging(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Trace);
                builder.AddProvider(captured);
            });
        }
        else
        {
            services.AddLogging();
        }

        services.AddSingleton(worker);
        if (edition is not null)
        {
            services.AddSingleton(edition);
        }
        services.AddComukiWorkers();

        // NOT a using-declaration: the harness owns disposal via
        // RegistryHarness.DisposeAsync, so the provider (and the scoped
        // factory every worker cycle resolves through) stays alive for
        // the whole test — mirrors ComukiWorkerRegistryShould's harness.
        var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<ComukiWorkerRegistry>();

        // Configure BEFORE StartAsync: the supervisor reads
        // DeferredRecheckInterval at its first delay, so an interval set
        // after StartAsync races the already-captured default (5s) and
        // can push promotion past the test's deadline.
        configure?.Invoke(registry);
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

    /// <summary>Hand-rolled capture provider — the registry logger flows through the standard MEL pipeline; capturing here is the canonical way to assert on warning text without standing up a third-party sink.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<LogEntry> entries = [];

        public ILogger CreateLogger(string categoryName)
        {
            return new CapturingLogger(categoryName, entries);
        }

        public void Dispose()
        {
        }

        public bool ContainsWarningMatching(Func<LogEntry, bool> predicate)
        {
            lock (entries)
            {
                return entries.Any(entry => entry.Level == LogLevel.Warning && predicate(entry));
            }
        }

        public bool ContainsInformationMatching(Func<LogEntry, bool> predicate)
        {
            lock (entries)
            {
                return entries.Any(entry => entry.Level == LogLevel.Information && predicate(entry));
            }
        }
    }

    private sealed record LogEntry(LogLevel Level, string Category, string Message, Exception? Exception);

    private sealed class CapturingLogger(string category, List<LogEntry> sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (sink)
            {
                sink.Add(new LogEntry(logLevel, category, formatter(state, exception), exception));
            }
        }
    }

    /// <summary>Hand-rolled fake: counts executions and exposes a Name/Schedule; mirrors <c>ComukiWorkerRegistryShould.CountingWorker</c> shape.</summary>
    private sealed class CountingWorker(string name) : IComukiWorker
    {
        private int executions;

        public string Name => name;

        public WorkerSchedule Schedule => WorkerSchedule.Interval(TimeSpan.FromMinutes(1));

        public int Executions => Volatile.Read(ref executions);

        public Task<WorkerResult> ExecuteAsync(WorkerContext context, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref executions);
            return Task.FromResult(WorkerResult.Ok("cycle"));
        }
    }

    /// <summary>
    /// Test-only gated fake: implements <see cref="IComukiWorker"/> and
    /// carries a class-level <see cref="RequiresFeatureAttribute"/> with
    /// the catalog's real <c>background-llm-watchers</c> key. Mirrors
    /// <c>AddComukiModuleShould.GatedMarker</c>: the key is the literal
    /// well-formed dash-case string, not <c>nameof(...)</c>.
    /// </summary>
    [RequiresFeature(BackgroundLlmWatchersKey)]
    private sealed class GatedWorker(string name) : IComukiWorker
    {
        private int executions;

        public string Name => name;

        public WorkerSchedule Schedule => WorkerSchedule.Interval(TimeSpan.FromMinutes(1));

        public int Executions => Volatile.Read(ref executions);

        public Task<WorkerResult> ExecuteAsync(WorkerContext context, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref executions);
            return Task.FromResult(WorkerResult.Ok("cycle"));
        }
    }
}
