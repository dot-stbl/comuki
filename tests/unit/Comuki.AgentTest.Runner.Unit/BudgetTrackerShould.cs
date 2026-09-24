using Comuki.AgentTest.Runner.Execution.Budget;
using Comuki.AgentTest.Runner.Reporting.Report;
using Shouldly;
using Xunit;

namespace Comuki.AgentTest.Runner.Unit;

/// <summary>
/// Pure-object tests for <see cref="BudgetTracker"/> — the mutable
/// accumulator the runner reads post-run and the recording server reads
/// pre-forward. Boundary semantics are: <see cref="BudgetCap.IsOverBudget"/>
/// uses strictly-greater-than, so a cap of N micro-USD flips true at
/// N+1 micro-USD, matching <c>CostAssertion</c>'s "may not have spent
/// more than" phrasing from the parent brief.
/// </summary>
public sealed class BudgetTrackerShould
{
    [Fact(DisplayName = "Given a fresh tracker under an unlimited cap, when reads happen before any Add, then Total is zero and IsOverBudget is false")]
    public void FreshTrackerStartsUnderBudget()
    {
        var tracker = new BudgetTracker(BudgetCap.Unlimited);

        tracker.Total.UsdMicros.ShouldBe(0L);
        tracker.Total.TokensIn.ShouldBe(0L);
        tracker.Total.TokensOut.ShouldBe(0L);
        tracker.IsOverBudget.ShouldBeFalse();
        tracker.Cap.IsBounded.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given multiple Adds, when Total is read, then tokens and USD accumulate correctly")]
    public void AddsAccumulateAcrossCalls()
    {
        var tracker = new BudgetTracker(BudgetCap.Resolve(scenarioMaxUsd: 1.00m, globalEnvVar: null));

        tracker.Add(new RunCost { UsdMicros = 100_000, TokensIn = 500, TokensOut = 250 });
        tracker.Add(new RunCost { UsdMicros = 200_000, TokensIn = 300, TokensOut = 400 });

        tracker.Total.UsdMicros.ShouldBe(300_000L);
        tracker.Total.TokensIn.ShouldBe(800L);
        tracker.Total.TokensOut.ShouldBe(650L);
    }

    [Fact(DisplayName = "Given observed USD equals the cap exactly, when IsOverBudget is read, then it is false (the rule is strictly-greater-than)")]
    public void EqualToCapIsNotOver()
    {
        var tracker = new BudgetTracker(BudgetCap.Resolve(scenarioMaxUsd: 0.10m, globalEnvVar: null));

        tracker.Add(new RunCost { UsdMicros = 100_000 });

        tracker.Total.UsdMicros.ShouldBe(100_000L);
        tracker.IsOverBudget.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given observed USD exceeds the cap by one micro-USD, when IsOverBudget is read, then it is true")]
    public void OneMicroOverIsOver()
    {
        var tracker = new BudgetTracker(BudgetCap.Resolve(scenarioMaxUsd: 0.10m, globalEnvVar: null));

        tracker.Add(new RunCost { UsdMicros = 100_001 });

        tracker.IsOverBudget.ShouldBeTrue();
    }

    [Fact(DisplayName = "Given observed USD stays at or below the cap across multiple Adds, when IsOverBudget is read at each step, then it stays false")]
    public void StaysUnderAcrossAdds()
    {
        var tracker = new BudgetTracker(BudgetCap.Resolve(scenarioMaxUsd: 0.10m, globalEnvVar: null));

        tracker.Add(new RunCost { UsdMicros = 50_000 });
        tracker.IsOverBudget.ShouldBeFalse();
        tracker.Add(new RunCost { UsdMicros = 49_999 });
        tracker.IsOverBudget.ShouldBeFalse();
        tracker.Add(new RunCost { UsdMicros = 1 });
        tracker.IsOverBudget.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given observed USD crosses the cap on the last Add, when IsOverBudget is read, then it is true")]
    public void CrossesCapOnLastAdd()
    {
        var tracker = new BudgetTracker(BudgetCap.Resolve(scenarioMaxUsd: 0.10m, globalEnvVar: null));

        tracker.Add(new RunCost { UsdMicros = 99_999 });
        tracker.Add(new RunCost { UsdMicros = 2 });

        tracker.Total.UsdMicros.ShouldBe(100_001L);
        tracker.IsOverBudget.ShouldBeTrue();
    }

    [Fact(DisplayName = "Given an unlimited cap, when Add pushes the total arbitrarily high, then IsOverBudget stays false forever")]
    public void UnlimitedNeverOver()
    {
        var tracker = new BudgetTracker(BudgetCap.Unlimited);

        tracker.Add(new RunCost { UsdMicros = long.MaxValue - 1 });

        tracker.IsOverBudget.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a delta with negative USD or token counts, when Add is called, then those fields are clamped to zero (not subtracted)")]
    public void NegativeDeltaClampedToZero()
    {
        var tracker = new BudgetTracker(BudgetCap.Unlimited);

        tracker.Add(new RunCost { UsdMicros = -100, TokensIn = -5, TokensOut = -3 });

        tracker.Total.UsdMicros.ShouldBe(0L);
        tracker.Total.TokensIn.ShouldBe(0L);
        tracker.Total.TokensOut.ShouldBe(0L);
    }

    [Fact(DisplayName = "Given the cap snapshot, when the tracker is constructed, then Cap is exposed and stable across Add calls")]
    public void CapSnapshotIsStable()
    {
        var cap = BudgetCap.Resolve(scenarioMaxUsd: 0.50m, globalEnvVar: null);
        var tracker = new BudgetTracker(cap);

        tracker.Cap.UsdMicros.ShouldBe(500_000L);
        tracker.Add(new RunCost { UsdMicros = 100_000 });
        tracker.Cap.UsdMicros.ShouldBe(500_000L);
    }
}
