using Comuki.AgentTest.Runner.Reporting.Report;

namespace Comuki.AgentTest.Runner.Reporting;

/// <summary>One scenario execution's outcome — the runner's per-scenario unit, aggregated by <see cref="RunReport.FromResults"/>.</summary>
public sealed record ScenarioResult
{
    /// <summary>The scenario's <see cref="Scenarios.ScenarioDefinition.Name"/>.</summary>
    public required string ScenarioName { get; init; }

    /// <summary>True when every assertion held.</summary>
    public bool Passed { get; init; }

    /// <summary>True when the scenario was intentionally not run (e.g. a T2b assertion blocked on an unmerged seam — <c>[Fact(Skip = "...")]</c> territory, not this type, but the shape is here for WS7).</summary>
    public bool Skipped { get; init; }

    /// <summary>Which assertion stage failed; null when <see cref="Passed"/> is true.</summary>
    public string? FailedStage { get; init; }

    /// <summary>The exact violated assertion, human-readable; null when <see cref="Passed"/> is true.</summary>
    public string? FailureMessage { get; init; }

    /// <summary>Evidence paths (relative to the report file) — transcript dump, journal timeline dump, container logs.</summary>
    public IReadOnlyList<string> ArtifactPaths { get; init; } = [];

    /// <summary>This scenario's cost contribution (zero for fake/T2a).</summary>
    public RunCost Cost { get; init; } = new();

    /// <summary>Wall-clock duration of this one scenario.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Builds a passing result.</summary>
    /// <param name="scenarioName"></param>
    /// <param name="duration"></param>
    /// <param name="artifactPaths"></param>
    /// <param name="cost">Observed cost contribution — defaults to zero for harnesses that never meter tokens.</param>
    public static ScenarioResult Success(string scenarioName, TimeSpan duration, IReadOnlyList<string> artifactPaths, RunCost? cost = null)
    {
        return new ScenarioResult
        {
            ScenarioName = scenarioName,
            Passed = true,
            Duration = duration,
            ArtifactPaths = artifactPaths,
            Cost = cost ?? new RunCost(),
        };
    }

    /// <summary>Builds a failing result naming the exact violated assertion.</summary>
    /// <param name="scenarioName"></param>
    /// <param name="stage"></param>
    /// <param name="message"></param>
    /// <param name="duration"></param>
    /// <param name="artifactPaths"></param>
    /// <param name="cost">Observed cost contribution — the budget-cap failure path uses this to report the actual spend alongside the cap.</param>
    public static ScenarioResult Failure(
        string scenarioName,
        string stage,
        string message,
        TimeSpan duration,
        IReadOnlyList<string> artifactPaths,
        RunCost? cost = null)
    {
        return new ScenarioResult
        {
            ScenarioName = scenarioName,
            Passed = false,
            FailedStage = stage,
            FailureMessage = message,
            Duration = duration,
            ArtifactPaths = artifactPaths,
            Cost = cost ?? new RunCost(),
        };
    }
}
