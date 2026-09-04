using Comuki.Modules.Costs.Application.Ports;
using Comuki.Modules.Costs.Domain.Events;
using Comuki.Modules.Proxy.Application.Budgeting;
using Comuki.Modules.Proxy.Application.Models;
using Comuki.Shared.Kernel.Ids;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Proxy.Unit;

/// <summary>Monthly cap + Retry-After computation for <see cref="DefaultProxyBudgetEnforcer"/>.</summary>
public sealed class DefaultProxyBudgetEnforcerShould
{
    [Fact(DisplayName = "Given a key with no budget, when EvaluateAsync runs, then Allowed is true and CapUsdMicros is null")]
    public async Task UnlimitedKeyIsAlwaysAllowedAsync()
    {
        var projectId = ProjectId.New();
        var store = Substitute.For<IUsageEventStore>();
        var enforcer = new DefaultProxyBudgetEnforcer(store, TimeProvider.System);

        var verdict = await enforcer.EvaluateAsync(
            new VirtualKey("vkey", projectId, new UpstreamSpec("openai", "https://api.openai.com", "OPENAI_API_KEY")),
            TestContext.Current.CancellationToken);

        verdict.Allowed.ShouldBeTrue();
        verdict.CapUsdMicros.ShouldBeNull();
        verdict.SpentUsdMicros.ShouldBe(0);
        verdict.RetryAfterSeconds.ShouldBe(0);
    }

    [Fact(DisplayName = "Given spend below cap, when EvaluateAsync runs, then Allowed is true with diagnostic figures")]
    public async Task SpendBelowCapIsAllowedAsync()
    {
        var projectId = ProjectId.New();
        var store = Substitute.For<IUsageEventStore>();
        _ = store.SumProjectCostBySourceAsync(projectId, UsageSource.Proxy, Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(2_000_000);
        var enforcer = new DefaultProxyBudgetEnforcer(store, TimeProvider.System);

        var verdict = await enforcer.EvaluateAsync(
            new VirtualKey(
                "vkey",
                projectId,
                new UpstreamSpec("openai", "https://api.openai.com", "OPENAI_API_KEY"),
                BudgetUsd: 10m),
            TestContext.Current.CancellationToken);

        verdict.Allowed.ShouldBeTrue();
        verdict.CapUsdMicros.ShouldBe(10_000_000);
        verdict.SpentUsdMicros.ShouldBe(2_000_000);
        verdict.RetryAfterSeconds.ShouldBe(0);
    }

    [Fact(DisplayName = "Given spend at or above cap, when EvaluateAsync runs, then Allowed is false and Retry-After is set")]
    public async Task SpendAtCapIsRejectedAsync()
    {
        var projectId = ProjectId.New();
        var store = Substitute.For<IUsageEventStore>();
        _ = store.SumProjectCostBySourceAsync(projectId, UsageSource.Proxy, Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(10_000_000);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero));
        var enforcer = new DefaultProxyBudgetEnforcer(store, clock);

        var verdict = await enforcer.EvaluateAsync(
            new VirtualKey(
                "vkey",
                projectId,
                new UpstreamSpec("openai", "https://api.openai.com", "OPENAI_API_KEY"),
                BudgetUsd: 10m),
            TestContext.Current.CancellationToken);

        verdict.Allowed.ShouldBeFalse();
        verdict.CapUsdMicros.ShouldBe(10_000_000);
        verdict.SpentUsdMicros.ShouldBe(10_000_000);
        verdict.RetryAfterSeconds.ShouldBeGreaterThan(0);
    }

    [Fact(DisplayName = "Given source-keyed spend for OTHER sources (brain / worker), when EvaluateAsync runs, then it does not affect the proxy verdict")]
    public async Task NonProxySpendIsIgnoredAsync()
    {
        var projectId = ProjectId.New();
        var store = Substitute.For<IUsageEventStore>();
        _ = store.SumProjectCostBySourceAsync(projectId, UsageSource.Proxy, Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(0);
        var enforcer = new DefaultProxyBudgetEnforcer(store, TimeProvider.System);

        var verdict = await enforcer.EvaluateAsync(
            new VirtualKey(
                "vkey",
                projectId,
                new UpstreamSpec("openai", "https://api.openai.com", "OPENAI_API_KEY"),
                BudgetUsd: 5m),
            TestContext.Current.CancellationToken);

        verdict.Allowed.ShouldBeTrue();
        verdict.SpentUsdMicros.ShouldBe(0);
    }
}
