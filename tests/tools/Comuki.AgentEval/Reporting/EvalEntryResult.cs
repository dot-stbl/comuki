using Comuki.AgentEval.Scoring;
using Comuki.AgentTest.Runner.Reporting.Report;

namespace Comuki.AgentEval.Reporting;

/// <summary>
/// One corpus entry's full result, aggregated for the
/// <see cref="EvalReport"/> and rendered into the markdown
/// report's per-entry section.
/// </summary>
/// <param name="ScenarioName">The scenario's <c>ScenarioDefinition.Name</c>.</param>
/// <param name="Difficulty">The free-text difficulty label from <see cref="Corpus.EvalExtension.Difficulty"/>.</param>
/// <param name="Passed">Final pass/fail from <see cref="EvalScore.Passed"/>.</param>
/// <param name="Score">The full <see cref="EvalScore"/>: deterministic verdicts, judge outcome, quality score.</param>
/// <param name="Cost">This entry's observed cost contribution (zero in fake / replay modes; populated in live mode from pi's <c>ResultEvent.CostUsd</c>).</param>
/// <param name="DurationMs">Wall-clock duration of this one scenario's run.</param>
/// <param name="ArtifactPaths">Paths an agent can open to see this entry's evidence (working dir, cassette, etc.).</param>
public sealed record EvalEntryResult(
    string ScenarioName,
    string Difficulty,
    bool Passed,
    EvalScore Score,
    RunCost Cost,
    long DurationMs,
    IReadOnlyList<string> ArtifactPaths);
