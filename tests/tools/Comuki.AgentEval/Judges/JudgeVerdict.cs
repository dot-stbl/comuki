namespace Comuki.AgentEval.Judges;

/// <summary>
/// One criterion's score inside an LLM-as-judge verdict payload.
/// </summary>
/// <param name="CriterionId">The criterion's stable identifier (must match one of the <c>EvalRubric.Criteria</c> ids declared in the corpus entry).</param>
/// <param name="Score">Per-criterion score in [0, 1].</param>
/// <param name="Rationale">Free-text justification the judge returned for this criterion's score.</param>
public sealed record JudgeCriterionScore(string CriterionId, double Score, string Rationale);

/// <summary>
/// The strict JSON verdict shape the LLM-as-judge client must return.
/// <c>OverallScore</c> in this type is always the scorer-recomputed
/// rubric-weighted average — the model's own <c>overallScore</c> field is
/// discarded, per <see cref="JudgeVerdictParser"/>.
/// </summary>
/// <param name="Scores">One <see cref="JudgeCriterionScore"/> per rubric criterion; the parser enforces an exact-id match against <c>EvalRubric.Criteria</c>.</param>
/// <param name="OverallScore">Scorer-recomputed rubric-weighted average in [0, 1]; never the model's self-reported value.</param>
/// <param name="Verdict">Either <c>"pass"</c> or <c>"fail"</c>.</param>
/// <param name="Notes">Free-text notes the judge returned.</param>
public sealed record JudgeVerdict(IReadOnlyList<JudgeCriterionScore> Scores, double OverallScore, string Verdict, string Notes);
