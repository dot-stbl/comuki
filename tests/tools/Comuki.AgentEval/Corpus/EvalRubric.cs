namespace Comuki.AgentEval.Corpus;

/// <summary>
/// The LLM-as-judge rubric block a corpus entry can declare in its
/// <c>eval:</c> section. Optional — an entry without a rubric is judged
/// by deterministic checks alone, and <c>Scoring.EvalScorer</c>'s
/// <c>Passed</c> collapses to <c>DeterministicPass</c>.
/// </summary>
/// <remarks>
/// Init-only properties so YamlDotNet can deserialize this type via
/// its parameterless constructor.
/// </remarks>
public sealed class EvalRubric
{
    /// <summary>Every criterion the judge must score; the parser enforces an exact-id match against this list.</summary>
    public List<EvalRubricCriterion> Criteria { get; init; } = [];

    /// <summary>Rubric-weighted <c>overallScore</c> threshold below which a scored rubric fails. 0..1.</summary>
    public double MinScore { get; init; }

    /// <summary>
    /// True iff this rubric has at least one criterion AND every criterion
    /// has a positive weight — the conditions under which the scorer can
    /// compute a rubric-weighted <c>overallScore</c>. Used by
    /// <c>Scoring.EvalScorer</c> as a guard before applying the rubric.
    /// </summary>
    public bool IsValid => Criteria.Count > 0 && Criteria.All(static criterion => criterion.Weight > 0);
}
