using Comuki.Host.Workers;
using Comuki.Modules.Identity.Application.Ports;
using Comuki.Shared.Bootstrap.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.OidcSweeper;

/// <summary>
/// Sweep contract (issue #4 tail): one cycle deletes rows past
/// <c>now - StateTtl</c> through <see cref="OidcStateSweeper.SweepOnceAsync"/>,
/// and the worker surface (<see cref="IComukiWorker.ExecuteAsync"/>) probes
/// the schema every cycle — a missing table fails the cycle with a
/// Critical log (Q30 / v1.1) instead of taking the host down, and the
/// registry backs off and retries. We never sleep real time; the loop
/// itself is covered by the registry suite in
/// <c>Comuki.Shared.Bootstrap.Unit</c>.
/// </summary>
public sealed class OidcStateSweeperShould
{
    private static readonly DateTimeOffset frozenNow = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given the configured TTL, when SweepOnceAsync runs, then the store is called with now minus the TTL")]
    public async Task SweepOnceUsesConfiguredCutoffAsync()
    {
        var store = Substitute.For<IOidcStateStore>();
        var sut = NewSweeper(store, NewOptions(stateTtl: TimeSpan.FromMinutes(5)));

        await sut.SweepOnceAsync(TestContext.Current.CancellationToken);

        await store.Received(1).DeleteExpiredAsync(
            Arg.Is<DateTimeOffset>(static cutoff => cutoff == frozenNow - TimeSpan.FromMinutes(5)),
            TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "Given a shorter TTL, when SweepOnceAsync runs, then the cutoff shifts accordingly")]
    public async Task SweepOnceRespectsShorterTtlAsync()
    {
        var store = Substitute.For<IOidcStateStore>();
        var sut = NewSweeper(store, NewOptions(stateTtl: TimeSpan.FromMinutes(1)));

        await sut.SweepOnceAsync(TestContext.Current.CancellationToken);

        await store.Received(1).DeleteExpiredAsync(
            Arg.Is<DateTimeOffset>(static cutoff => cutoff == frozenNow - TimeSpan.FromMinutes(1)),
            TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "Given the table is present, when ExecuteAsync runs, then the cycle succeeds and no Critical log is emitted")]
    public async Task ExecuteSucceedsWhenTablePresentAsync()
    {
        var store = Substitute.For<IOidcStateStore>();
        store.TableExistsAsync(Arg.Any<CancellationToken>()).Returns(true);
        var logger = new RecordingLogger<OidcStateSweeper>();
        var sut = NewSweeper(store, NewOptions(), logger);

        var result = await ExecuteOnceAsync(sut);

        result.Success.ShouldBeTrue();
        await store.Received(1).TableExistsAsync(Arg.Any<CancellationToken>());
        logger.Records.Any(static entry => entry.Level == LogLevel.Critical).ShouldBeFalse();
    }

    [Fact(DisplayName = "Given the table is missing, when ExecuteAsync runs, then the cycle fails and a Critical log with the remediation hint is emitted")]
    public async Task ExecuteFailsWithCriticalWhenTableMissingAsync()
    {
        // Q30 / v1.1: a fresh deploy whose migrator has not yet run lands
        // here — the cycle fails, the Critical log carries the exact
        // remediation hint, and the registry retries with backoff (the
        // host does NOT refuse to start).
        var store = Substitute.For<IOidcStateStore>();
        store.TableExistsAsync(Arg.Any<CancellationToken>()).Returns(false);
        var logger = new RecordingLogger<OidcStateSweeper>();
        var sut = NewSweeper(store, NewOptions(), logger);

        var result = await ExecuteOnceAsync(sut);

        result.Success.ShouldBeFalse();
        result.Detail.ShouldNotBeNull();
        result.Detail.ShouldContain("oidc_states");
        await store.DidNotReceiveWithAnyArgs().DeleteExpiredAsync(default, TestContext.Current.CancellationToken);
        logger.Records.Any(static entry =>
            entry.Level == LogLevel.Critical
            && entry.Message.Contains("oidc_states", StringComparison.OrdinalIgnoreCase)).ShouldBeTrue();
    }

    [Fact(DisplayName = "Given a probe that throws a database error, when ExecuteAsync runs, then the cycle fails instead of crashing the caller")]
    public async Task ExecuteFailsWhenProbeThrowsAsync()
    {
        var store = Substitute.For<IOidcStateStore>();
        store.TableExistsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(new TimeoutException("probe timeout")));
        var sut = NewSweeper(store, NewOptions());

        var result = await ExecuteOnceAsync(sut);

        result.Success.ShouldBeFalse();
    }

    private static async Task<WorkerResult> ExecuteOnceAsync(OidcStateSweeper sut)
    {
        return await sut.ExecuteAsync(NewContext(), TestContext.Current.CancellationToken);
    }

    private static WorkerContext NewContext()
    {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();

        return new WorkerContext(provider, new FrozenTime(frozenNow), NullLogger.Instance);
    }

    private static OidcStateSweeper NewSweeper(IOidcStateStore store, OidcSweepOptions options, ILogger<OidcStateSweeper>? logger = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(store);
        var provider = services.BuildServiceProvider();

        return new OidcStateSweeper(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            new FrozenTime(frozenNow),
            logger ?? NullLogger<OidcStateSweeper>.Instance);
    }

    private static OidcSweepOptions NewOptions(TimeSpan? interval = null, TimeSpan? stateTtl = null, bool enabled = true)
    {
        return new OidcSweepOptions
        {
            Enabled = enabled,
            Interval = interval ?? TimeSpan.FromMinutes(5),
            StateTtl = stateTtl ?? TimeSpan.FromMinutes(5),
        };
    }

    private sealed class FrozenTime(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }

    /// <summary>In-process logger that captures every entry — used to assert Critical on the missing-table probe (Q30).</summary>
    private sealed class RecordingLogger<T> : ILogger<T>
    {
        private readonly List<(LogLevel Level, string Message)> records = [];

        public IReadOnlyList<(LogLevel Level, string Message)> Records => records;

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return new NoopDisposable();
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            records.Add((logLevel, formatter(state, exception)));
        }

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
