using Comuki.Host.Workers;
using Comuki.Modules.Identity.Application.Ports;
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
/// <c>now - StateTtl</c>, the loop honours the configured interval, and
/// the disabled flag short-circuits. We never sleep real time — the
/// loop test passes a <see cref="TimeSpan.Zero"/> interval and a
/// cancellation token to break after one cycle.
/// <para>
/// Issue Q30 / v1.1: at startup the sweeper probes
/// <see cref="IOidcStateStore.TableExistsAsync"/>. A missing table is
/// logged as <c>Critical</c> and the loop continues — the host does
/// not refuse to start.
/// </para>
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

    [Fact(DisplayName = "Given a zero interval, when ExecuteAsync runs, then the loop calls the store at least once before cancellation")]
    public async Task ExecuteAsyncLoopsUntilCancellationAsync()
    {
        var store = Substitute.For<IOidcStateStore>();
        _ = store.TableExistsAsync(Arg.Any<CancellationToken>()).Returns(true);
        var sut = NewSweeper(store, NewOptions(interval: TimeSpan.Zero));

        await RunWorkerUntilCancelledAsync(sut);

        await store.Received().DeleteExpiredAsync(
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given the disabled flag, when ExecuteAsync runs, then the loop never enters and the store is never called")]
    public async Task ExecuteAsyncDoesNothingWhenDisabledAsync()
    {
        var store = Substitute.For<IOidcStateStore>();
        var sut = NewSweeper(store, NewOptions(interval: TimeSpan.Zero, enabled: false));

        await RunWorkerUntilCancelledAsync(sut);

        await store.DidNotReceiveWithAnyArgs().DeleteExpiredAsync(default, TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "Given the table is present, when ExecuteAsync starts, then no Critical log is emitted and the loop continues")]
    public async Task StartupProbeReportsWhenTablePresentAsync()
    {
        // Issue Q30 / v1.1: the probe fires once at startup. When the
        // table is present, no Critical log is emitted and the loop
        // proceeds normally.
        var store = Substitute.For<IOidcStateStore>();
        _ = store.TableExistsAsync(Arg.Any<CancellationToken>()).Returns(true);
        var logger = new RecordingLogger<OidcStateSweeper>();
        var sut = NewSweeper(store, NewOptions(interval: TimeSpan.Zero), logger);

        await RunWorkerUntilCancelledAsync(sut);

        await store.Received(1).TableExistsAsync(Arg.Any<CancellationToken>());
        logger.Records.Any(static entry => entry.Level == LogLevel.Critical).ShouldBeFalse();
    }

    [Fact(DisplayName = "Given the table is missing, when ExecuteAsync starts, then a Critical log is emitted and the host continues running")]
    public async Task StartupProbeLogsCriticalWhenTableMissingAsync()
    {
        // Issue Q30 / v1.1: a fresh deploy whose migrator has not yet
        // run lands here, the probe returns false, and the sweeper
        // logs Critical with the exact remediation hint. The host
        // does NOT refuse to start — the loop continues and re-probes
        // on every cycle.
        var store = Substitute.For<IOidcStateStore>();
        _ = store.TableExistsAsync(Arg.Any<CancellationToken>()).Returns(false);
        var logger = new RecordingLogger<OidcStateSweeper>();
        var sut = NewSweeper(store, NewOptions(interval: TimeSpan.Zero), logger);

        await RunWorkerUntilCancelledAsync(sut);

        await store.Received(1).TableExistsAsync(Arg.Any<CancellationToken>());
        logger.Records.Any(static entry =>
            entry.Level == LogLevel.Critical
            && entry.Message.Contains("oidc_states", StringComparison.OrdinalIgnoreCase)).ShouldBeTrue();
    }

    private static async Task RunWorkerUntilCancelledAsync(OidcStateSweeper sut)
    {
        using var cts = new CancellationTokenSource();
        await sut.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
        await cts.CancelAsync();
        await sut.StopAsync(CancellationToken.None);
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

    /// <summary>In-process logger that captures every entry — used to assert Critical at startup (Q30 / v1.1).</summary>
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
