using Comuki.AgentEval.Scoring;
using Comuki.AgentTest.Runner.Reporting.Report;

namespace Comuki.AgentEval.Reporting;

/// <summary>
/// One corpus entry's full result, aggregated for the
/// <see cref="EvalReport"/> and rendered into the markdown
/// report's per-entry section.
/// </summary>
/// <remarks>
/// Init-only properties (no positional primary constructor) so
/// reflection-based materializers (xunit object construction in
/// some scenarios, future source-gen tooling) work without a
/// parameterless-constructor exception.
/// </remarks>
public sealed class EvalEntryResult
{
    /// <summary>The scenario's <c>ScenarioDefinition.Name</c>.</summary>
    public required string ScenarioName { get; init; }

    /// <summary>The free-text difficulty label from <c>EvalExtension.Difficulty</c>.</summary>
    public required string Difficulty { get; init; }

    /// <summary>Final pass/fail from <see cref="EvalScore.Passed"/>.</summary>
    public required bool Passed { get; init; }

    /// <summary>The full <see cref="EvalScore"/>: deterministic verdicts, judge outcome, quality score.</summary>
    public required EvalScore Score { get; init; }

    /// <summary>This entry's observed cost contribution (zero in fake / replay modes; populated in live mode from pi's <c>ResultEvent.CostUsd</c>).</summary>
    public required RunCost Cost { get; init; }

    /// <summary>Wall-clock duration of this one scenario's run.</summary>
    public required long DurationMs { get; init; }

    /// <summary>Paths an agent can open to see this entry's evidence (working dir, cassette, etc.).</summary>
    public required IReadOnlyList<string> ArtifactPaths { get; init; }
}
