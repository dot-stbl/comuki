namespace Comuki.AgentTest.Runner.Scenarios;

/// <summary>LLM-as-judge rubric assertion — T4/WS10 only.</summary>
public sealed record JudgeAssertion
{
    /// <summary>Rubric file path (relative to the scenario file).</summary>
    public string Rubric { get; init; } = string.Empty;

    /// <summary>Minimum passing score, 0..1.</summary>
    public double MinScore { get; init; }
}
