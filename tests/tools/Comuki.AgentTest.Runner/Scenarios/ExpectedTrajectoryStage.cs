namespace Comuki.AgentTest.Runner.Scenarios;

/// <summary>One expected trajectory stage — asserted against the tools pi/TestFakePi actually invoked in that stage.</summary>
public sealed record ExpectedTrajectoryStage
{
    /// <summary>Stage name (e.g. <c>plan</c>, <c>work-item</c>).</summary>
    public string Stage { get; init; } = string.Empty;

    /// <summary>Minimum tool calls expected in this stage; null = unchecked.</summary>
    public int? MinToolCalls { get; init; }

    /// <summary>Maximum tool calls expected in this stage; null = unchecked.</summary>
    public int? MaxToolCalls { get; init; }

    /// <summary>Tool names that must appear at least once in this stage; empty = unchecked.</summary>
    public List<string> ToolsUsed { get; init; } = [];

    /// <summary>Tool names that must never appear in this stage (worker-sdk lock/allowlist proof — T2b/WS7).</summary>
    public List<string> ForbiddenTools { get; init; } = [];
}
