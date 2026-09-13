using Comuki.Modules.Costs.Application.Ports;
using Comuki.Modules.Costs.Application.Queries;
using Comuki.Shared.Kernel.Ids;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Costs.Unit;

/// <summary>Read-model assembly of <see cref="GetPlatformCostsHandler"/>.</summary>
public sealed class GetPlatformCostsHandlerShould
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return now;
        }
    }

    private static IPlatformCostAggregator AggregatorReturning(
        long windowTotal,
        long allTimeTotal,
        IReadOnlyList<ProjectCostSlice> byProject,
        IReadOnlyList<DayCostSlice> byDay)
    {
        var aggregator = Substitute.For<IPlatformCostAggregator>();
        aggregator.SumAllCostUsdMicrosAsync(Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(windowTotal);
        aggregator.SumAllCostUsdMicrosAsync(null, Arg.Any<CancellationToken>())
            .Returns(allTimeTotal);
        aggregator.ListProjectSlicesAsync(Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(byProject);
        aggregator.ListDaySlicesAsync(Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(byDay);
        return aggregator;
    }

    [Fact(DisplayName = "Given slices, when HandleAsync with default window, then the rollup mirrors them over 30 days")]
    public async Task AssembleRollupWithDefaultWindowAsync()
    {
        var now = new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);
        var projectId = ProjectId.New();
        var aggregator = AggregatorReturning(
            windowTotal: 500,
            allTimeTotal: 900,
            byProject: [new ProjectCostSlice(projectId, 500, 3)],
            byDay: [new DayCostSlice(new DateOnly(2026, 9, 12), 500)]);

        var view = await new GetPlatformCostsHandler(aggregator, new FixedTimeProvider(now)).HandleAsync(
            windowDays: null,
            TestContext.Current.CancellationToken);

        view.WindowDays.ShouldBe(GetPlatformCostsHandler.DefaultWindowDays);
        view.Since.ShouldBe(now.AddDays(-GetPlatformCostsHandler.DefaultWindowDays));
        view.WindowUsdMicros.ShouldBe(500);
        view.AllTimeUsdMicros.ShouldBe(900);
        view.ByProject.ShouldHaveSingleItem().ProjectId.ShouldBe(projectId.Value);
        view.ByProject.ShouldHaveSingleItem().Runs.ShouldBe(3);
        view.ByDay.ShouldHaveSingleItem().Date.ShouldBe(new DateOnly(2026, 9, 12));
    }

    [Fact(DisplayName = "Given a requested window, when HandleAsync, then the since instant uses that many days back")]
    public async Task UseRequestedWindowAsync()
    {
        var now = new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);
        var aggregator = AggregatorReturning(0, 0, [], []);

        var view = await new GetPlatformCostsHandler(aggregator, new FixedTimeProvider(now)).HandleAsync(
            windowDays: 7,
            TestContext.Current.CancellationToken);

        view.WindowDays.ShouldBe(7);
        view.Since.ShouldBe(now.AddDays(-7));
    }

    [Theory(DisplayName = "Given out-of-range windows, when HandleAsync, then the window clamps to [1, 365]")]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(400, 365)]
    public async Task ClampWindowAsync(int requested, int expected)
    {
        var now = new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);
        var aggregator = AggregatorReturning(0, 0, [], []);

        var view = await new GetPlatformCostsHandler(aggregator, new FixedTimeProvider(now)).HandleAsync(
            requested,
            TestContext.Current.CancellationToken);

        view.WindowDays.ShouldBe(expected);
    }
}
