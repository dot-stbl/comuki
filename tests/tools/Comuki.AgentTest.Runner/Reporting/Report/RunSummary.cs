namespace Comuki.AgentTest.Runner.Reporting.Report;

/// <summary>Pass/fail/skip counts of a <see cref="RunReport"/>.</summary>
public sealed record RunSummary
{
    /// <summary>Total scenarios run.</summary>
    public int Total { get; init; }

    /// <summary>Scenarios that passed every assertion.</summary>
    public int Passed { get; init; }

    /// <summary>Scenarios that failed at least one assertion.</summary>
    public int Failed { get; init; }

    /// <summary>Scenarios skipped (e.g. a T2b assertion blocked on an unmerged production seam).</summary>
    public int Skipped { get; init; }
}
