namespace Comuki.AgentTest.Runner.Scenarios;

/// <summary>Final-status assertion against the completed work item / run.</summary>
public sealed record RunAssertion
{
    /// <summary>Expected final work-item status: <c>succeeded</c> or <c>failed</c> (matches <c>WorkItemStatus</c>, case-insensitive).</summary>
    public string FinalStatus { get; init; } = "succeeded";
}
