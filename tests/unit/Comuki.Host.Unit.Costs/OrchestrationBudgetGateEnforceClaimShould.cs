using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Host.Costs;
using Comuki.Shared.Contracts.Costs;
using Comuki.Shared.Contracts.Journal;
using Comuki.Shared.Contracts.Usage;
using Comuki.Shared.Kernel.Exceptions;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.Costs;

/// <summary>
/// <see cref="OrchestrationBudgetGate.EnforceClaimAsync"/> contract
/// (Q20 / Q24): a soft-budget over-spend logs a warning and lets the
/// claim proceed; a hard-budget over-spend throws a
/// <see cref="BudgetExceededException"/> the central exception handler
/// maps to HTTP 402 Payment Required.
/// </summary>
public sealed class OrchestrationBudgetGateEnforceClaimShould
{
    private static readonly DateTimeOffset frozenNow = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given no caps configured, when EnforceClaimAsync runs, then it returns without throwing")]
    public async Task AllowWhenNoCapsConfiguredAsync()
    {
        var budgets = Substitute.For<IProjectBudgetSettings>();
        var usage = Substitute.For<IUsageEventStore>();
        var journal = Substitute.For<IRunJournal>();
        _ = budgets.GetAsync(Arg.Any<ProjectId>(), Arg.Any<CancellationToken>())
            .Returns(new ProjectBudgetCaps(null, null));
        _ = usage.SumProjectCostUsdMicrosAsync(Arg.Any<ProjectId>(), since: Arg.Any<DateTimeOffset?>(), cancellationToken: Arg.Any<CancellationToken>())
            .Returns(1_000L);
        var gate = NewGate(budgets, usage, journal);

        await gate.EnforceClaimAsync(ProjectId.New(), TestContext.Current.CancellationToken);

        await usage.Received(1).SumProjectCostUsdMicrosAsync(Arg.Any<ProjectId>(), since: Arg.Any<DateTimeOffset?>(), cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given spend under both caps, when EnforceClaimAsync runs, then it returns without throwing or emitting a journal row")]
    public async Task AllowUnderBothCapsAsync()
    {
        var projectId = ProjectId.New();
        var budgets = Substitute.For<IProjectBudgetSettings>();
        var usage = Substitute.For<IUsageEventStore>();
        var journal = Substitute.For<IRunJournal>();
        _ = budgets.GetAsync(projectId, Arg.Any<CancellationToken>())
            .Returns(new ProjectBudgetCaps(SoftLimitUsdMicros: 10_000_000, HardLimitUsdMicros: 20_000_000));
        _ = usage.SumProjectCostUsdMicrosAsync(projectId, Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(5_000_000L);
        var gate = NewGate(budgets, usage, journal);

        await gate.EnforceClaimAsync(projectId, TestContext.Current.CancellationToken);

        await journal.DidNotReceiveWithAnyArgs().AppendAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "Given spend meets the soft cap but not the hard cap, when EnforceClaimAsync runs, then it returns without throwing")]
    public async Task AllowSoftBudgetExceededAsync()
    {
        var projectId = ProjectId.New();
        var budgets = Substitute.For<IProjectBudgetSettings>();
        var usage = Substitute.For<IUsageEventStore>();
        var journal = Substitute.For<IRunJournal>();
        _ = budgets.GetAsync(projectId, Arg.Any<CancellationToken>())
            .Returns(new ProjectBudgetCaps(SoftLimitUsdMicros: 10_000_000, HardLimitUsdMicros: 20_000_000));
        _ = usage.SumProjectCostUsdMicrosAsync(projectId, Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(15_000_000L);
        var gate = NewGate(budgets, usage, journal);

        await gate.EnforceClaimAsync(projectId, TestContext.Current.CancellationToken);

        await journal.DidNotReceiveWithAnyArgs().AppendAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "Given spend meets the hard cap exactly, when EnforceClaimAsync runs, then it throws BudgetExceededException with code budget.hard_exceeded")]
    public async Task ThrowOnHardBoundaryAsync()
    {
        var projectId = ProjectId.New();
        var budgets = Substitute.For<IProjectBudgetSettings>();
        var usage = Substitute.For<IUsageEventStore>();
        var journal = Substitute.For<IRunJournal>();
        _ = budgets.GetAsync(projectId, Arg.Any<CancellationToken>())
            .Returns(new ProjectBudgetCaps(SoftLimitUsdMicros: 10_000_000, HardLimitUsdMicros: 20_000_000));
        _ = usage.SumProjectCostUsdMicrosAsync(projectId, Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(20_000_000L);
        var gate = NewGate(budgets, usage, journal);

        var exception = await Should.ThrowAsync<BudgetExceededException>(
            () => gate.EnforceClaimAsync(projectId, TestContext.Current.CancellationToken));

        exception.Code.ShouldBe(OrchestrationBudgetGate.HardExceededCode);
        exception.Message.ShouldContain("hard budget exceeded");
        await journal.DidNotReceiveWithAnyArgs().AppendAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "Given spend exceeds the hard cap, when EnforceClaimAsync runs, then it throws BudgetExceededException and never appends a journal row (HardStopAsync is the journal path)")]
    public async Task ThrowOverHardAsync()
    {
        var projectId = ProjectId.New();
        var budgets = Substitute.For<IProjectBudgetSettings>();
        var usage = Substitute.For<IUsageEventStore>();
        var journal = Substitute.For<IRunJournal>();
        _ = budgets.GetAsync(projectId, Arg.Any<CancellationToken>())
            .Returns(new ProjectBudgetCaps(SoftLimitUsdMicros: 10_000_000, HardLimitUsdMicros: 20_000_000));
        _ = usage.SumProjectCostUsdMicrosAsync(projectId, Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(25_000_000L);
        var gate = NewGate(budgets, usage, journal);

        await Should.ThrowAsync<BudgetExceededException>(
            () => gate.EnforceClaimAsync(projectId, TestContext.Current.CancellationToken));

        await journal.DidNotReceiveWithAnyArgs().AppendAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "Given only the soft cap set, when spend exceeds it, then EnforceClaimAsync still returns without throwing")]
    public async Task SoftOnlyAllowsOverSpendAsync()
    {
        var projectId = ProjectId.New();
        var budgets = Substitute.For<IProjectBudgetSettings>();
        var usage = Substitute.For<IUsageEventStore>();
        var journal = Substitute.For<IRunJournal>();
        _ = budgets.GetAsync(projectId, Arg.Any<CancellationToken>())
            .Returns(new ProjectBudgetCaps(SoftLimitUsdMicros: 10_000_000, HardLimitUsdMicros: null));
        _ = usage.SumProjectCostUsdMicrosAsync(projectId, Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(15_000_000L);
        var gate = NewGate(budgets, usage, journal);

        await gate.EnforceClaimAsync(projectId, TestContext.Current.CancellationToken);
    }

    private static OrchestrationBudgetGate NewGate(
        IProjectBudgetSettings budgets,
        IUsageEventStore usage,
        IRunJournal journal)
    {
        // EnforceClaimAsync doesn't touch the DbContext — only HardStopAsync
        // does. A bare DbContextOptions instance is enough for the gate's
        // constructor to accept it.
        var contextOptions = new DbContextOptionsBuilder<OrchestrationDbContext>().Options;
        var context = new OrchestrationDbContext(contextOptions);
        return new OrchestrationBudgetGate(
            context,
            journal,
            budgets,
            usage,
            new FrozenTime(frozenNow),
            NullLogger<OrchestrationBudgetGate>.Instance);
    }

    private sealed class FrozenTime(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
