using Comuki.AgentTest.Runner.Reporting;
using Comuki.AgentTest.Runner.Reporting.Report;
using Shouldly;
using Xunit;

namespace Comuki.AgentTest.Runner.Unit;

/// <summary>Proves <see cref="RunReport.FromResults"/>'s aggregation — the design.md "Report format" envelope, built from <see cref="ScenarioResult"/>s.</summary>
public sealed class RunReportShould
{
    [Fact(DisplayName = "Given one passing and one failing scenario, when a report is built, then the summary counts and failures list match")]
    public void AggregatePassAndFailCounts()
    {
        var results = new[]
        {
            ScenarioResult.Success("add-null-check", TimeSpan.FromSeconds(5), ["add-null-check.timeline.json"]),
            ScenarioResult.Failure("bad-image-label", "compute.start", "no such image", TimeSpan.FromMilliseconds(200), []),
        };

        var report = RunReport.FromResults("agent-loop", "fake", DateTimeOffset.UtcNow, TimeSpan.FromSeconds(6), results);

        report.SchemaVersion.ShouldBe(1);
        report.Tier.ShouldBe("agent-loop");
        report.Mode.ShouldBe("fake");
        report.Summary.Total.ShouldBe(2);
        report.Summary.Passed.ShouldBe(1);
        report.Summary.Failed.ShouldBe(1);
        report.Summary.Skipped.ShouldBe(0);
        report.Failures.ShouldHaveSingleItem();
        report.Failures[0].Scenario.ShouldBe("bad-image-label");
        report.Failures[0].Stage.ShouldBe("compute.start");
        report.Failures[0].Message.ShouldBe("no such image");
    }

    [Fact(DisplayName = "Given a skipped scenario, when a report is built, then it counts as skipped, not failed")]
    public void CountSkippedSeparatelyFromFailed()
    {
        var results = new[]
        {
            new ScenarioResult { ScenarioName = "blocked-on-seam", Skipped = true, Duration = TimeSpan.Zero },
        };

        var report = RunReport.FromResults("agent-loop", "fake", DateTimeOffset.UtcNow, TimeSpan.Zero, results);

        report.Summary.Total.ShouldBe(1);
        report.Summary.Skipped.ShouldBe(1);
        report.Summary.Failed.ShouldBe(0);
        report.Failures.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given per-scenario costs, when a report is built, then usdMicros/tokens sum across scenarios")]
    public void SumCostAcrossScenarios()
    {
        var results = new[]
        {
            ScenarioResult.Success("a", TimeSpan.Zero, []) with { Cost = new RunCost { UsdMicros = 100, TokensIn = 10, TokensOut = 5 } },
            ScenarioResult.Success("b", TimeSpan.Zero, []) with { Cost = new RunCost { UsdMicros = 50, TokensIn = 3, TokensOut = 2 } },
        };

        var report = RunReport.FromResults("agent-loop", "fake", DateTimeOffset.UtcNow, TimeSpan.Zero, results);

        report.Cost.UsdMicros.ShouldBe(150);
        report.Cost.TokensIn.ShouldBe(13);
        report.Cost.TokensOut.ShouldBe(7);
    }
}
