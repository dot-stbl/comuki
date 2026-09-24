namespace Comuki.AgentEval.Judges;

/// <summary>
/// One deterministic judge's verdict on a corpus entry. <see cref="Passed"/>
/// is a tri-state: <c>true</c> = the asserted invariant holds,
/// <c>false</c> = the asserted invariant is violated,
/// <c>null</c> = the judge did not apply (e.g. the corpus entry omitted
/// the field the judge would assert on). Downstream code — the markdown
/// renderer, the <c>Scoring.EvalScorer</c> — must treat <c>null</c> as
/// "not-applicable", distinct from both pass and fail.
/// </summary>
public sealed record DeterministicVerdict
{
    /// <summary>Stable identifier for the judge function that produced this verdict (e.g. <c>diff-applies</c>, <c>files-within-allowed-set</c>).</summary>
    public required string JudgeName { get; init; }

    /// <summary>Tri-state verdict: <c>true</c> pass, <c>false</c> fail, <c>null</c> not-applicable.</summary>
    public bool? Passed { get; init; }

    /// <summary>Human-readable reason — pass, failure cause, or a brief note when not-applicable.</summary>
    public required string Message { get; init; }
}
