namespace Comuki.AgentTest.Runner.Scenarios;

/// <summary>Live-mode budget ceiling — WS9 only.</summary>
public sealed record ScenarioBudget
{
    /// <summary>Maximum USD the runner lets a live run spend before aborting mid-run.</summary>
    public decimal MaxUsd { get; init; }
}
