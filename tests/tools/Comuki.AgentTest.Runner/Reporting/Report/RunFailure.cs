namespace Comuki.AgentTest.Runner.Reporting.Report;

/// <summary>One failed scenario's report entry.</summary>
public sealed record RunFailure
{
    /// <summary>The failing scenario's name.</summary>
    public string Scenario { get; init; } = string.Empty;

    /// <summary>Which assertion stage failed (e.g. <c>assertions.journal</c>, <c>assertions.run</c>, <c>claim</c>).</summary>
    public string Stage { get; init; } = string.Empty;

    /// <summary>Human-readable failure reason naming the exact violated assertion.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Paths (relative to the report) an agent can open to see the failure's evidence — transcript, timeline dump, diff.</summary>
    public IReadOnlyList<string> ArtifactPaths { get; init; } = [];
}
