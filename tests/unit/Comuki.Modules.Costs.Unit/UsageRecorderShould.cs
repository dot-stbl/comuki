using Comuki.Modules.Costs.Application.Ports;
using Comuki.Modules.Costs.Application.Recording;
using Comuki.Modules.Costs.Domain.Events;
using Comuki.Shared.Contracts.Costs;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Comuki.Modules.Costs.Unit;

/// <summary>Budget soft/hard behaviour of <see cref="UsageRecorder"/>.</summary>
public sealed class UsageRecorderShould
{
    [Fact(DisplayName = "Given hard budget exceeded with a run, when RecordAsync, then hard-stop is invoked")]
    public async Task HardStopWhenHardBudgetExceededAsync()
    {
        var projectId = ProjectId.New();
        var runId = RunId.New();
        var store = Substitute.For<IUsageEventStore>();
        var budgets = Substitute.For<IProjectBudgetSettings>();
        var gate = Substitute.For<IBudgetGate>();

        _ = budgets.GetAsync(projectId, Arg.Any<CancellationToken>())
            .Returns(new ProjectBudgetCaps(SoftLimitUsdMicros: 500_000, HardLimitUsdMicros: 1_000_000));
        _ = store.SumProjectCostUsdMicrosAsync(projectId, null, Arg.Any<CancellationToken>())
            .Returns(1_250_000);

        var recorder = new UsageRecorder(store, budgets, gate, NullLogger<UsageRecorder>.Instance);
        var cancellationToken = TestContext.Current.CancellationToken;

        await recorder.RecordAsync(
            new UsageRecord(projectId, runId, UsageSourceKeys.Proxy, "model", 10, 5, 250_000, DateTimeOffset.UtcNow),
            cancellationToken);

        await store.Received(1).AddAsync(Arg.Any<UsageEvent>(), Arg.Any<CancellationToken>());
        await gate.Received(1).HardStopAsync(runId, projectId, 1_250_000, 1_000_000, Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given soft budget only exceeded, when RecordAsync, then hard-stop is not invoked")]
    public async Task SoftExceedanceDoesNotHardStopAsync()
    {
        var projectId = ProjectId.New();
        var store = Substitute.For<IUsageEventStore>();
        var budgets = Substitute.For<IProjectBudgetSettings>();
        var gate = Substitute.For<IBudgetGate>();

        _ = budgets.GetAsync(projectId, Arg.Any<CancellationToken>())
            .Returns(new ProjectBudgetCaps(SoftLimitUsdMicros: 100_000, HardLimitUsdMicros: 10_000_000));
        _ = store.SumProjectCostUsdMicrosAsync(projectId, null, Arg.Any<CancellationToken>())
            .Returns(200_000);

        var recorder = new UsageRecorder(store, budgets, gate, NullLogger<UsageRecorder>.Instance);
        var cancellationToken = TestContext.Current.CancellationToken;

        await recorder.RecordAsync(
            new UsageRecord(projectId, RunId.New(), UsageSourceKeys.Brain, "model", 1, 1, 50_000, DateTimeOffset.UtcNow),
            cancellationToken);

        await gate.DidNotReceive().HardStopAsync(
            Arg.Any<RunId>(),
            Arg.Any<ProjectId>(),
            Arg.Any<long>(),
            Arg.Any<long>(),
            Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given hard exceedance without run attribution, when RecordAsync, then hard-stop is skipped")]
    public async Task SkipHardStopWithoutRunAsync()
    {
        var projectId = ProjectId.New();
        var store = Substitute.For<IUsageEventStore>();
        var budgets = Substitute.For<IProjectBudgetSettings>();
        var gate = Substitute.For<IBudgetGate>();

        _ = budgets.GetAsync(projectId, Arg.Any<CancellationToken>())
            .Returns(new ProjectBudgetCaps(null, HardLimitUsdMicros: 1));
        _ = store.SumProjectCostUsdMicrosAsync(projectId, null, Arg.Any<CancellationToken>())
            .Returns(100);

        var recorder = new UsageRecorder(store, budgets, gate, NullLogger<UsageRecorder>.Instance);
        var cancellationToken = TestContext.Current.CancellationToken;

        await recorder.RecordAsync(
            new UsageRecord(projectId, null, UsageSourceKeys.System, "model", 0, 0, 100, DateTimeOffset.UtcNow),
            cancellationToken);

        await gate.DidNotReceive().HardStopAsync(
            Arg.Any<RunId>(),
            Arg.Any<ProjectId>(),
            Arg.Any<long>(),
            Arg.Any<long>(),
            Arg.Any<CancellationToken>());
    }
}
