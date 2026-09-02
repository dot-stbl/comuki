using Comuki.Modules.Costs.Application.Ports;
using Comuki.Modules.Costs.Application.Queries;
using Comuki.Modules.Costs.Domain.Events;
using Comuki.Shared.Contracts.Costs;
using Comuki.Shared.Kernel.Ids;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Costs.Unit;

/// <summary>Read-model assembly of <see cref="GetProjectCostsHandler"/>.</summary>
public sealed class GetProjectCostsHandlerShould
{
    [Fact(DisplayName = "Given soft and hard caps exceeded, when HandleAsync, then both exceeded flags are true")]
    public async Task MarkSoftAndHardExceededAsync()
    {
        var projectId = ProjectId.New();
        var store = Substitute.For<IUsageEventStore>();
        var budgets = Substitute.For<IProjectBudgetSettings>();
        var recent = UsageEvent.Create(
            projectId,
            RunId.New(),
            UsageSource.Proxy,
            "model",
            1,
            2,
            100,
            DateTimeOffset.UtcNow);

        _ = budgets.GetAsync(projectId, Arg.Any<CancellationToken>())
            .Returns(new ProjectBudgetCaps(SoftLimitUsdMicros: 50, HardLimitUsdMicros: 80));
        _ = store.SumProjectCostUsdMicrosAsync(projectId, null, Arg.Any<CancellationToken>()).Returns(100);
        _ = store.ListRecentAsync(projectId, GetProjectCostsHandler.DefaultRecentTake, Arg.Any<CancellationToken>())
            .Returns([recent]);

        var view = await new GetProjectCostsHandler(store, budgets).HandleAsync(
            projectId,
            TestContext.Current.CancellationToken);

        view.ProjectId.ShouldBe(projectId);
        view.SpentUsdMicros.ShouldBe(100);
        view.SoftLimitUsdMicros.ShouldBe(50);
        view.HardLimitUsdMicros.ShouldBe(80);
        view.SoftExceeded.ShouldBeTrue();
        view.HardExceeded.ShouldBeTrue();
        view.Recent.ShouldHaveSingleItem().Model.ShouldBe("model");
    }

    [Fact(DisplayName = "Given unlimited caps, when HandleAsync, then exceeded flags stay false")]
    public async Task KeepUnlimitedBelowCapsAsync()
    {
        var projectId = ProjectId.New();
        var store = Substitute.For<IUsageEventStore>();
        var budgets = Substitute.For<IProjectBudgetSettings>();

        _ = budgets.GetAsync(projectId, Arg.Any<CancellationToken>())
            .Returns(new ProjectBudgetCaps(null, null));
        _ = store.SumProjectCostUsdMicrosAsync(projectId, null, Arg.Any<CancellationToken>()).Returns(999_999);
        _ = store.ListRecentAsync(projectId, GetProjectCostsHandler.DefaultRecentTake, Arg.Any<CancellationToken>())
            .Returns([]);

        var view = await new GetProjectCostsHandler(store, budgets).HandleAsync(
            projectId,
            TestContext.Current.CancellationToken);

        view.SoftExceeded.ShouldBeFalse();
        view.HardExceeded.ShouldBeFalse();
        view.Recent.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given spend below soft, when HandleAsync, then SoftExceeded is false")]
    public async Task SoftNotExceededBelowCapAsync()
    {
        var projectId = ProjectId.New();
        var store = Substitute.For<IUsageEventStore>();
        var budgets = Substitute.For<IProjectBudgetSettings>();

        _ = budgets.GetAsync(projectId, Arg.Any<CancellationToken>())
            .Returns(new ProjectBudgetCaps(SoftLimitUsdMicros: 1_000, HardLimitUsdMicros: 10_000));
        _ = store.SumProjectCostUsdMicrosAsync(projectId, null, Arg.Any<CancellationToken>()).Returns(999);
        _ = store.ListRecentAsync(projectId, GetProjectCostsHandler.DefaultRecentTake, Arg.Any<CancellationToken>())
            .Returns([]);

        var view = await new GetProjectCostsHandler(store, budgets).HandleAsync(
            projectId,
            TestContext.Current.CancellationToken);

        view.SoftExceeded.ShouldBeFalse();
        view.HardExceeded.ShouldBeFalse();
    }
}
