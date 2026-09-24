using Comuki.AgentEval.Judges;

namespace Comuki.AgentEval.Scoring;

/// <summary>
/// One corpus entry's combined scoring result: the deterministic
/// verdicts, the LLM-as-judge outcome, the merged quality score,
/// and the final pass/fail flag after applying the merge rules in
/// <see cref="EvalScorer"/>.
/// </summary>
public sealed record EvalScore(
    bool DeterministicPass,
    IReadOnlyList<DeterministicVerdict> Deterministic,
    JudgeOutcome Judge,
    double QualityScore,
    bool Passed);
