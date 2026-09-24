using Comuki.AgentTest.Runner.Execution.Budget;
using Shouldly;
using Xunit;

namespace Comuki.AgentTest.Runner.Unit;

/// <summary>
/// Pure-object tests for <see cref="BudgetCap.Resolve"/> — the
/// scenario-cap-vs-global-ceiling combination rule the WS9 brief spells
/// out explicitly. No env mutations leak across tests because each test
/// sets its own unique env-var name and tears it down.
/// </summary>
public sealed class BudgetCapShould
{
    [Fact(DisplayName = "Given neither cap set, when Resolve is called, then the cap is Unlimited (no enforcement)")]
    public void UnlimitedWhenNeitherSet()
    {
        var envVar = UniqueEnvVar(nameof(UnlimitedWhenNeitherSet));

        var cap = BudgetCap.Resolve(scenarioMaxUsd: null, globalEnvVar: envVar);

        cap.UsdMicros.ShouldBeNull();
        cap.IsBounded.ShouldBeFalse();
        cap.IsOverBudget(observedUsdMicros: long.MaxValue).ShouldBeFalse();
    }

    [Fact(DisplayName = "Given only a scenario cap, when Resolve is called, then the effective cap equals the scenario cap")]
    public void ScenarioOnlyWhenEnvUnset()
    {
        var envVar = UniqueEnvVar(nameof(ScenarioOnlyWhenEnvUnset));
        EnsureUnset(envVar);

        var cap = BudgetCap.Resolve(scenarioMaxUsd: 0.25m, globalEnvVar: envVar);

        cap.UsdMicros.ShouldBe(250_000L);
        cap.IsBounded.ShouldBeTrue();
        cap.IsOverBudget(250_001L).ShouldBeTrue();
        cap.IsOverBudget(250_000L).ShouldBeFalse();
    }

    [Fact(DisplayName = "Given only a global env cap, when Resolve is called, then the effective cap equals the parsed env value")]
    public void EnvOnlyWhenScenarioUnset()
    {
        var envVar = UniqueEnvVar(nameof(EnvOnlyWhenScenarioUnset));
        Environment.SetEnvironmentVariable(envVar, "0.10");

        try
        {
            var cap = BudgetCap.Resolve(scenarioMaxUsd: null, globalEnvVar: envVar);

            cap.UsdMicros.ShouldBe(100_000L);
            cap.IsBounded.ShouldBeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVar, null);
        }
    }

    [Fact(DisplayName = "Given both caps set with scenario tighter, when Resolve is called, then the smaller (scenario) wins")]
    public void SmallerWinsWhenScenarioTighter()
    {
        var envVar = UniqueEnvVar(nameof(SmallerWinsWhenScenarioTighter));
        Environment.SetEnvironmentVariable(envVar, "5.00");

        try
        {
            var cap = BudgetCap.Resolve(scenarioMaxUsd: 0.25m, globalEnvVar: envVar);

            cap.UsdMicros.ShouldBe(250_000L);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVar, null);
        }
    }

    [Fact(DisplayName = "Given both caps set with global tighter, when Resolve is called, then the smaller (global) wins")]
    public void SmallerWinsWhenGlobalTighter()
    {
        var envVar = UniqueEnvVar(nameof(SmallerWinsWhenGlobalTighter));
        Environment.SetEnvironmentVariable(envVar, "0.05");

        try
        {
            var cap = BudgetCap.Resolve(scenarioMaxUsd: 10.00m, globalEnvVar: envVar);

            cap.UsdMicros.ShouldBe(50_000L);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVar, null);
        }
    }

    [Fact(DisplayName = "Given both caps equal, when Resolve is called, then either (they tie) wins — the value is the same")]
    public void EqualCapsResolveToThatValue()
    {
        var envVar = UniqueEnvVar(nameof(EqualCapsResolveToThatValue));
        Environment.SetEnvironmentVariable(envVar, "0.50");

        try
        {
            var cap = BudgetCap.Resolve(scenarioMaxUsd: 0.50m, globalEnvVar: envVar);

            cap.UsdMicros.ShouldBe(500_000L);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVar, null);
        }
    }

    [Fact(DisplayName = "Given a malformed env value, when Resolve is called, then the env is treated as unset (no crash)")]
    public void MalformedEnvTreatedAsUnset()
    {
        var envVar = UniqueEnvVar(nameof(MalformedEnvTreatedAsUnset));
        Environment.SetEnvironmentVariable(envVar, "not-a-number");

        try
        {
            var cap = BudgetCap.Resolve(scenarioMaxUsd: 0.20m, globalEnvVar: envVar);

            cap.UsdMicros.ShouldBe(200_000L);
            cap.IsBounded.ShouldBeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVar, null);
        }
    }

    [Fact(DisplayName = "Given an empty-string env value, when Resolve is called, then the env is treated as unset")]
    public void EmptyEnvTreatedAsUnset()
    {
        var envVar = UniqueEnvVar(nameof(EmptyEnvTreatedAsUnset));
        Environment.SetEnvironmentVariable(envVar, string.Empty);

        try
        {
            var cap = BudgetCap.Resolve(scenarioMaxUsd: 0.15m, globalEnvVar: envVar);

            cap.UsdMicros.ShouldBe(150_000L);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVar, null);
        }
    }

    [Fact(DisplayName = "Given a negative env value, when Resolve is called, then the env is treated as unset (negative cap is meaningless)")]
    public void NegativeEnvTreatedAsUnset()
    {
        var envVar = UniqueEnvVar(nameof(NegativeEnvTreatedAsUnset));
        Environment.SetEnvironmentVariable(envVar, "-1.00");

        try
        {
            var cap = BudgetCap.Resolve(scenarioMaxUsd: 0.10m, globalEnvVar: envVar);

            cap.UsdMicros.ShouldBe(100_000L);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVar, null);
        }
    }

    [Fact(DisplayName = "Given a null env-var name, when Resolve is called, then no env read is attempted and the scenario-only branch fires")]
    public void NullEnvVarNameBypassesEnv()
    {
        var cap = BudgetCap.Resolve(scenarioMaxUsd: 0.30m, globalEnvVar: null);

        cap.UsdMicros.ShouldBe(300_000L);
    }

    [Fact(DisplayName = "Given the Unlimited sentinel, when IsOverBudget is called, then it never flips true")]
    public void UnlimitedNeverOverBudget()
    {
        var cap = BudgetCap.Unlimited;

        cap.IsOverBudget(0L).ShouldBeFalse();
        cap.IsOverBudget(1L).ShouldBeFalse();
        cap.IsOverBudget(long.MaxValue).ShouldBeFalse();
    }

    private static string UniqueEnvVar(string testName)
    {
        return $"COMUKI_TEST_BUDGETCAP_{testName}_{Guid.NewGuid():N}";
    }

    private static void EnsureUnset(string name)
    {
        Environment.SetEnvironmentVariable(name, null);
    }
}
