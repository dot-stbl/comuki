using Comuki.AgentTest.Runner.Execution;
using Comuki.AgentTest.Runner.Reporting.Report;
using Comuki.AgentTest.Runner.Scenarios;
using Shouldly;
using Xunit;

namespace Comuki.AgentTest.Runner.Unit;

/// <summary>
/// Tests for the WS9 cost + budget cap integration on
/// <see cref="ScenarioRunner"/> — exercised against an in-file fake
/// <see cref="IAgentLoopHarness"/> so no Podman, no orchestrator host, and
/// no HTTP server actually runs. The "fake upstream" the parent brief
/// expects is <see cref="FakeAgentLoopHarness"/>, which surfaces any
/// <see cref="RunCost"/> the test scripts into it.
/// </summary>
public sealed class ScenarioRunnerBudgetShould
{
    [Fact(DisplayName = "Given a fake harness reporting over-budget USD and a scenario with a budget cap, when RunAsync runs, then it fails at stage 'budget' and the result carries the observed cost")]
    public async Task BudgetCapFailsAtBudgetStageAsync()
    {
        var harness = new FakeAgentLoopHarness { CostToReturn = new RunCost { UsdMicros = 250_000, TokensIn = 1000, TokensOut = 500 } };
        var runner = new ScenarioRunner(harness);
        var scenario = new ScenarioDefinition
        {
            Name = "live-budget-cap-trip",
            Worker = new ScenarioWorker { Image = "img:tag", ProfileKey = "p", ProfilesRef = "r" },
            Budget = new ScenarioBudget { MaxUsd = 0.10m },
            Assertions = new ScenarioAssertions { Run = new RunAssertion { FinalStatus = "succeeded" } },
        };

        var result = await runner.RunAsync(scenario, new ScenarioRunOptions(), CancellationToken.None);

        result.Passed.ShouldBeFalse();
        result.FailedStage.ShouldBe("budget");
        (result.FailureMessage ?? string.Empty).ShouldContain("250000");
        (result.FailureMessage ?? string.Empty).ShouldContain("100000");
        result.Cost.UsdMicros.ShouldBe(250_000L);
        result.Cost.TokensIn.ShouldBe(1000L);
        result.Cost.TokensOut.ShouldBe(500L);
    }

    [Fact(DisplayName = "Given a fake harness reporting at-budget USD exactly, when RunAsync runs against a scenario with a matching cap, then the scenario passes")]
    public async Task BudgetCapAtCapIsAllowedAsync()
    {
        var harness = new FakeAgentLoopHarness { CostToReturn = new RunCost { UsdMicros = 100_000 } };
        var runner = new ScenarioRunner(harness);
        var scenario = new ScenarioDefinition
        {
            Name = "live-budget-at-cap",
            Worker = new ScenarioWorker { Image = "img:tag", ProfileKey = "p", ProfilesRef = "r" },
            Budget = new ScenarioBudget { MaxUsd = 0.10m },
            Assertions = new ScenarioAssertions { Run = new RunAssertion { FinalStatus = "succeeded" } },
        };

        var result = await runner.RunAsync(scenario, new ScenarioRunOptions(), CancellationToken.None);

        result.Passed.ShouldBeTrue();
        result.Cost.UsdMicros.ShouldBe(100_000L);
    }

    [Fact(DisplayName = "Given a fake harness reporting over-budget USD and an assertions.cost ceiling below it, when RunAsync runs, then it fails at stage 'assertions.cost'")]
    public async Task CostAssertionFailsAtAssertionsCostStageAsync()
    {
        var harness = new FakeAgentLoopHarness { CostToReturn = new RunCost { UsdMicros = 200_000 } };
        var runner = new ScenarioRunner(harness);
        var scenario = new ScenarioDefinition
        {
            Name = "cost-ceiling-trip",
            Worker = new ScenarioWorker { Image = "img:tag", ProfileKey = "p", ProfilesRef = "r" },
            Assertions = new ScenarioAssertions
            {
                Run = new RunAssertion { FinalStatus = "succeeded" },
                Cost = new CostAssertion { MaxUsdMicros = 100_000 },
            },
        };

        var result = await runner.RunAsync(scenario, new ScenarioRunOptions(), CancellationToken.None);

        result.Passed.ShouldBeFalse();
        result.FailedStage.ShouldBe("assertions.cost");
        (result.FailureMessage ?? string.Empty).ShouldContain("200000");
        (result.FailureMessage ?? string.Empty).ShouldContain("100000");
    }

    [Fact(DisplayName = "Given a fake harness reporting under-ceiling USD and an assertions.cost ceiling above it, when RunAsync runs, then the scenario passes")]
    public async Task CostAssertionUnderCeilingPassesAsync()
    {
        var harness = new FakeAgentLoopHarness { CostToReturn = new RunCost { UsdMicros = 50_000 } };
        var runner = new ScenarioRunner(harness);
        var scenario = new ScenarioDefinition
        {
            Name = "cost-ceiling-ok",
            Worker = new ScenarioWorker { Image = "img:tag", ProfileKey = "p", ProfilesRef = "r" },
            Assertions = new ScenarioAssertions
            {
                Run = new RunAssertion { FinalStatus = "succeeded" },
                Cost = new CostAssertion { MaxUsdMicros = 100_000 },
            },
        };

        var result = await runner.RunAsync(scenario, new ScenarioRunOptions(), CancellationToken.None);

        result.Passed.ShouldBeTrue();
        result.Cost.UsdMicros.ShouldBe(50_000L);
    }

    [Fact(DisplayName = "Given a fake harness reporting cost and a scenario declaring neither budget nor assertions.cost, when RunAsync runs, then the result carries the observed cost through Passed with stage=null")]
    public async Task CostThreadsThroughOnSuccessWithoutAnyAssertionAsync()
    {
        var harness = new FakeAgentLoopHarness { CostToReturn = new RunCost { UsdMicros = 7_777, TokensIn = 11, TokensOut = 22 } };
        var runner = new ScenarioRunner(harness);
        var scenario = new ScenarioDefinition
        {
            Name = "cost-thread",
            Worker = new ScenarioWorker { Image = "img:tag", ProfileKey = "p", ProfilesRef = "r" },
            Assertions = new ScenarioAssertions { Run = new RunAssertion { FinalStatus = "succeeded" } },
        };

        var result = await runner.RunAsync(scenario, new ScenarioRunOptions(), CancellationToken.None);

        result.Passed.ShouldBeTrue();
        result.FailedStage.ShouldBeNull();
        result.Cost.UsdMicros.ShouldBe(7_777L);
        result.Cost.TokensIn.ShouldBe(11L);
        result.Cost.TokensOut.ShouldBe(22L);
    }

    [Fact(DisplayName = "Given a fake harness reporting cost and a scenario whose Run assertion fails, when RunAsync runs, then the result still carries the observed cost under stage 'assertions.run'")]
    public async Task CostThreadsThroughOnFailedRunAssertionAsync()
    {
        var harness = new FakeAgentLoopHarness(workItemStatus: "Failed")
        {
            CostToReturn = new RunCost { UsdMicros = 99_999 },
        };
        var runner = new ScenarioRunner(harness);
        var scenario = new ScenarioDefinition
        {
            Name = "cost-on-run-failure",
            Worker = new ScenarioWorker { Image = "img:tag", ProfileKey = "p", ProfilesRef = "r" },
            Assertions = new ScenarioAssertions { Run = new RunAssertion { FinalStatus = "succeeded" } },
        };

        var result = await runner.RunAsync(scenario, new ScenarioRunOptions(), CancellationToken.None);

        result.Passed.ShouldBeFalse();
        result.FailedStage.ShouldBe("assertions.run");
        result.Cost.UsdMicros.ShouldBe(99_999L);
    }
}
