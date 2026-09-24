namespace Comuki.AgentEval.Corpus;

/// <summary>
/// One weighted criterion in an <see cref="EvalRubric"/>; the LLM-as-judge
/// client emits one <c>scores[].score</c> per criterion, the scorer
/// normalizes the weights, and the rubric-weighted average becomes the
/// judge's authoritative <see cref="Judges.JudgeVerdict.OverallScore"/>.
/// </summary>
/// <remarks>
/// Init-only properties (no positional primary constructor) so
/// YamlDotNet's <c>DefaultObjectFactory</c> can materialize this type
/// via its parameterless constructor.
/// </remarks>
public sealed class EvalRubricCriterion
{
    /// <summary>Stable, kebab-case criterion identifier — used as the JSON key in the judge's verdict payload and validated by the parser.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Human-readable description of what this criterion measures; surfaced verbatim into the LLM judge's user prompt.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Non-negative relative weight. The scorer normalizes the sum of weights to 1 before averaging, so weights are NOT required to sum to 1 in the file.</summary>
    public double Weight { get; init; }
}
