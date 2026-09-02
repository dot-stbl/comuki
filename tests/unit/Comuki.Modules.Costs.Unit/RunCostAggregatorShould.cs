using Comuki.Modules.Costs.Application.Aggregation;
using Comuki.Modules.Costs.Application.Ports;
using Comuki.Shared.Kernel.Ids;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Costs.Unit;

/// <summary>Pure store-forwarding of <see cref="RunCostAggregator"/>.</summary>
public sealed class RunCostAggregatorShould
{
    [Fact(DisplayName = "Given a run id, when SumRunAsync is called, then the store sum is returned")]
    public async Task ForwardRunSumAsync()
    {
        var runId = RunId.New();
        var store = Substitute.For<IUsageEventStore>();
        _ = store.SumRunCostUsdMicrosAsync(runId, Arg.Any<CancellationToken>()).Returns(42_000);

        var total = await new RunCostAggregator(store).SumRunAsync(runId, TestContext.Current.CancellationToken);

        total.ShouldBe(42_000);
        await store.Received(1).SumRunCostUsdMicrosAsync(runId, Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a project id and since, when SumProjectAsync is called, then the store is queried with since")]
    public async Task ForwardProjectSumWithSinceAsync()
    {
        var projectId = ProjectId.New();
        var since = DateTimeOffset.Parse("2026-09-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var store = Substitute.For<IUsageEventStore>();
        _ = store.SumProjectCostUsdMicrosAsync(projectId, since, Arg.Any<CancellationToken>()).Returns(7);

        var total = await new RunCostAggregator(store).SumProjectAsync(
            projectId,
            since,
            TestContext.Current.CancellationToken);

        total.ShouldBe(7);
        await store.Received(1).SumProjectCostUsdMicrosAsync(projectId, since, Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given no since, when SumProjectAsync is called, then the store is queried with null since")]
    public async Task ForwardProjectSumWithoutSinceAsync()
    {
        var projectId = ProjectId.New();
        var store = Substitute.For<IUsageEventStore>();
        _ = store.SumProjectCostUsdMicrosAsync(projectId, null, Arg.Any<CancellationToken>()).Returns(9);

        var total = await new RunCostAggregator(store).SumProjectAsync(
            projectId,
            cancellationToken: TestContext.Current.CancellationToken);

        total.ShouldBe(9);
        await store.Received(1).SumProjectCostUsdMicrosAsync(projectId, null, Arg.Any<CancellationToken>());
    }
}
