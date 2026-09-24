using Comuki.AgentEval.Corpus;
using Comuki.AgentEval.Judges;

namespace Comuki.AgentEval.Scoring;

/// <summary>
/// Pure, offline merger of <see cref="DeterministicVerdict"/>s and a
/// <see cref="JudgeOutcome"/> into one <see cref="EvalScore"/>.
/// </summary>
/// <remarks>
/// <para>
/// The <c>Passed</c> rules, in order:
/// <list type="number">
///   <item>Deterministic gate — <c>DeterministicPass = false</c> forces <c>Passed = false</c> regardless of judge outcome.</item>
///   <item>Deterministic pass + judge scored → passed iff rubric-scored <see cref="Judges.JudgeVerdict.OverallScore"/> &gt;= <see cref="EvalRubric.MinScore"/>.</item>
///   <item>Deterministic pass + judge error with declared rubric → <c>Passed = false</c>: a mandatory rubric that the judge couldn't evaluate is a failure, not a free pass.</item>
///   <item>Deterministic pass + judge skipped with declared rubric → <c>Passed = DeterministicPass</c>: missing live env is "skip", not "fail" (matches every other tier's "no live env = skip, not fail" rule). The markdown report must visibly note this case so a human reading it knows quality was not fully checked.</item>
///   <item>Deterministic pass + no rubric declared → <c>Passed = DeterministicPass</c> unconditionally.</item>
/// </list>
/// </para>
/// <para>
/// The <c>QualityScore</c> formula (deliberately simple v1, not tuned):
/// <list type="bullet">
///   <item>Judge did not score: <c>QualityScore = DeterministicPass ? 1.0 : 0.0</c>.</item>
///   <item>Judge scored: <c>QualityScore = (DeterministicPass ? 1.0 : 0.0) * 0.5 + judge.OverallScore * 0.5</c>.</item>
/// </list>
/// Deterministic failures cap <c>QualityScore</c> at 0.5 even with a
/// generous judge score; a perfect judge on a perfectly deterministic
/// passing run still tops out at 1.0.
/// </para>
/// </remarks>
public static class EvalScorer
{
    /// <summary>Combines <paramref name="deterministic"/> and <paramref name="judge"/> into a single <see cref="EvalScore"/> per the rules above.</summary>
    public static EvalScore Score(
        CorpusEntry entry,
        IReadOnlyList<DeterministicVerdict> deterministic,
        JudgeOutcome judge)
    {
        var deterministicPass = deterministic.All(verdict => verdict.Passed is null or true);

        var qualityScore = ComputeQualityScore(deterministicPass, judge);
        var passed = ComputePassed(entry, deterministicPass, judge);

        return new EvalScore(
            DeterministicPass: deterministicPass,
            Deterministic: deterministic,
            Judge: judge,
            QualityScore: qualityScore,
            Passed: passed);
    }

    private static double ComputeQualityScore(bool deterministicPass, JudgeOutcome judge) => judge.Kind switch
    {
        JudgeOutcomeKind.Scored when judge.Verdict is not null =>
            (deterministicPass ? 1.0 : 0.0) * 0.5 + judge.Verdict.OverallScore * 0.5,
        _ => deterministicPass ? 1.0 : 0.0,
    };

    private static bool ComputePassed(CorpusEntry entry, bool deterministicPass, JudgeOutcome judge) =>
        !deterministicPass
            ? false
            : entry.Eval.Rubric is null
                ? true
                : judge.Kind switch
                {
                    JudgeOutcomeKind.Scored when judge.Verdict is not null =>
                        judge.Verdict.OverallScore >= entry.Eval.Rubric.MinScore,
                    JudgeOutcomeKind.Error => false,
                    JudgeOutcomeKind.Skipped => true,
                    _ => true,
                };
}
