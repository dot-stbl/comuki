namespace Comuki.AgentTest.Runner.Execution;

/// <summary>Timing/artifact knobs for one <see cref="ScenarioRunner.RunAsync"/> call.</summary>
public sealed record ScenarioRunOptions
{
    /// <summary>
    /// Total wall-clock budget for the work item to reach a terminal status.
    /// Default (90s) allows for the real container's cold start plus
    /// <c>TranslatorOptions.ClaimPollInterval</c>'s built-in 10s default (no
    /// <c>COMUKI_*</c> env override exists for it today — see the WS6
    /// report's runtime notes).
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(90);

    /// <summary>Delay between journal/status polls.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Directory the run's journal timeline is dumped to as
    /// <c>&lt;scenario&gt;.timeline.json</c> (relative paths of what gets
    /// written land in <see cref="Reporting.ScenarioResult.ArtifactPaths"/>).
    /// Null skips the dump.
    /// </summary>
    public string? ArtifactsDirectory { get; init; }
}
