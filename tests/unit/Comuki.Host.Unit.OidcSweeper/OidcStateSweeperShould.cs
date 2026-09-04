using Comuki.Host.Workers;
using Comuki.Modules.Identity.Application.Ports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Comuki.Host.Unit.OidcSweeper;

/// <summary>
/// Sweep contract (issue #4 tail): one cycle deletes rows past
/// <c>now - StateTtl</c>, the loop honours the configured interval, and
/// the disabled flag short-circuits. We never sleep real time — the
/// loop test passes a <see cref="TimeSpan.Zero"/> interval and a
/// cancellation token to break after one cycle.
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

    private static async Task RunWorkerUntilCancelledAsync(OidcStateSweeper sut)
    {
        using var cts = new CancellationTokenSource();
        await sut.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
        await cts.CancelAsync();
        await sut.StopAsync(CancellationToken.None);
    }

    private static OidcStateSweeper NewSweeper(IOidcStateStore store, OidcSweepOptions options)
    {
        var services = new ServiceCollection();
        services.AddSingleton(store);
        var provider = services.BuildServiceProvider();

        return new OidcStateSweeper(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            new FrozenTime(frozenNow),
            NullLogger<OidcStateSweeper>.Instance);
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
}
