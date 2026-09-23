namespace Comuki.AgentTest.Runner.Reporting.Report;

/// <summary>Aggregate cost across a report's scenarios (zero in fake mode; populated once WS9 lands live-mode wiring).</summary>
public sealed record RunCost
{
    /// <summary>Micro-USD spent (1,000,000 = $1).</summary>
    public long UsdMicros { get; init; }

    /// <summary>Total input tokens billed.</summary>
    public long TokensIn { get; init; }

    /// <summary>Total output tokens billed.</summary>
    public long TokensOut { get; init; }
}
