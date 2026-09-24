namespace Comuki.AgentTest.Runner.Scenarios;

/// <summary>Cost-ceiling assertion — replay/live only (WS8/WS9).</summary>
public sealed record CostAssertion
{
    /// <summary>Maximum micro-USD the run may have spent.</summary>
    public long? MaxUsdMicros { get; init; }
}
