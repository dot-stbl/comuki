using Comuki.Modules.Costs.Application;
using Comuki.Modules.Costs.Application.Aggregation;
using Comuki.Modules.Costs.Application.Budgets;
using Comuki.Modules.Costs.Application.Ports;
using Comuki.Modules.Costs.Application.Queries;
using Comuki.Modules.Costs.Application.Recording;
using Comuki.Shared.Contracts.Costs;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Costs.Unit;

/// <summary>Default budget ports and application DI composition.</summary>
public sealed class BudgetDefaultsShould
{
    [Fact(DisplayName = "Given NullBudgetGate, when HardStopAsync is called, then it completes without work")]
    public async Task NullGateIsNoOpAsync()
    {
        await new NullBudgetGate().HardStopAsync(
            RunId.New(),
            ProjectId.New(),
            spentUsdMicros: 1,
            hardLimitUsdMicros: 1,
            TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "Given UnlimitedBudgetSettings, when GetAsync is called, then both caps are null")]
    public async Task UnlimitedCapsAreNullAsync()
    {
        var caps = await new UnlimitedBudgetSettings().GetAsync(ProjectId.New(), TestContext.Current.CancellationToken);

        caps.SoftLimitUsdMicros.ShouldBeNull();
        caps.HardLimitUsdMicros.ShouldBeNull();
    }

    [Fact(DisplayName = "Given AddCostsApplication, when resolved, then defaults and scoped services are registered")]
    public void RegisterApplicationDefaults()
    {
        var services = new ServiceCollection();
        _ = services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        _ = services.AddSingleton(Substitute.For<IUsageEventStore>());
        _ = services.AddCostsApplication();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IBudgetGate>().ShouldBeOfType<NullBudgetGate>();
        provider.GetRequiredService<IProjectBudgetSettings>().ShouldBeOfType<UnlimitedBudgetSettings>();
        provider.GetRequiredService<IUsageRecorder>().ShouldBeOfType<UsageRecorder>();
        provider.GetRequiredService<RunCostAggregator>().ShouldNotBeNull();
        provider.GetRequiredService<GetProjectCostsHandler>().ShouldNotBeNull();
    }
}
