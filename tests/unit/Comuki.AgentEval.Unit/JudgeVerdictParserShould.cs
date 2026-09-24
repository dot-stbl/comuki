using Comuki.AgentEval.Corpus;
using Comuki.AgentEval.Judges;
using Shouldly;
using Xunit;

namespace Comuki.AgentEval.Unit;

/// <summary>
/// Exercises <see cref="JudgeVerdictParser.TryParse"/> against every
/// documented failure mode: extra criterion id, missing criterion id,
/// out-of-range score, malformed JSON, and — critically — the
/// override behavior where the parser's recomputed <c>OverallScore</c>
/// wins over the model's self-reported value when the two disagree.
/// </summary>
public sealed class JudgeVerdictParserShould
{
    [Fact(DisplayName = "Given valid JSON matching the rubric exactly, when parsed, then TryParse returns true and OverallScore equals the rubric-weighted average")]
    public void ParseValidVerdict()
    {
        var rubric = new EvalRubric
        {
            Criteria =
            [
                new EvalRubricCriterion { Id = "a", Description = "criterion a", Weight = 1.0 },
                new EvalRubricCriterion { Id = "b", Description = "criterion b", Weight = 2.0 },
            ],
            MinScore = 0.5,
        };
        var json =
            """
            {
              "scores": [
                { "criterionId": "a", "score": 1.0, "rationale": "great" },
                { "criterionId": "b", "score": 0.5, "rationale": "ok" }
              ],
              "overallScore": 0.99,
              "verdict": "pass",
              "notes": "fine"
            }
            """;

        var ok = JudgeVerdictParser.TryParse(json, rubric, out var verdict, out var error);

        ok.ShouldBeTrue();
        error.ShouldBeNull();
        verdict.ShouldNotBeNull();
        verdict!.Scores.Count.ShouldBe(2);
        // Recomputed weighted average = (1.0 * 1.0 + 0.5 * 2.0) / 3.0 = 0.6666...
        verdict.OverallScore.ShouldBeInRange(0.666, 0.667);
    }

    [Fact(DisplayName = "Given the model's self-reported overallScore disagrees with the per-criterion scores, when parsed, then OverallScore is the recomputed rubric-weighted average (model's value discarded)")]
    public void RecomputedOverallScoreDisagreesWithModel()
    {
        var rubric = new EvalRubric
        {
            Criteria = [new EvalRubricCriterion { Id = "only", Description = "The only criterion.", Weight = 1.0 }],
            MinScore = 0.5,
        };
        var json =
            """
            {
              "scores": [
                { "criterionId": "only", "score": 1.0, "rationale": "perfect" }
              ],
              "overallScore": 0.01,
              "verdict": "pass",
              "notes": ""
            }
            """;

        var ok = JudgeVerdictParser.TryParse(json, rubric, out var verdict, out var error);

        ok.ShouldBeTrue();
        verdict!.OverallScore.ShouldBe(1d);
    }

    [Fact(DisplayName = "Given JSON with an extra criterion id not in the rubric, when parsed, then TryParse returns false with a non-null error")]
    public void RejectExtraCriterionId()
    {
        var rubric = new EvalRubric
        {
            Criteria = [new EvalRubricCriterion { Id = "only", Description = "The only criterion.", Weight = 1.0 }],
            MinScore = 0.5,
        };
        var json =
            """
            {
              "scores": [
                { "criterionId": "only", "score": 1.0, "rationale": "ok" },
                { "criterionId": "extra", "score": 0.5, "rationale": "ok" }
              ],
              "overallScore": 0.5,
              "verdict": "pass",
              "notes": ""
            }
            """;

        var ok = JudgeVerdictParser.TryParse(json, rubric, out _, out var error);

        ok.ShouldBeFalse();
        error.ShouldNotBeNull();
        error.ShouldContain("extra");
    }

    [Fact(DisplayName = "Given JSON with a missing criterion id, when parsed, then TryParse returns false with a non-null error")]
    public void RejectMissingCriterionId()
    {
        var rubric = new EvalRubric
        {
            Criteria =
            [
                new EvalRubricCriterion { Id = "a", Description = "a", Weight = 1.0 },
                new EvalRubricCriterion { Id = "b", Description = "b", Weight = 1.0 },
            ],
            MinScore = 0.5,
        };
        var json =
            """
            {
              "scores": [
                { "criterionId": "a", "score": 1.0, "rationale": "ok" }
              ],
              "overallScore": 0.5,
              "verdict": "pass",
              "notes": ""
            }
            """;

        var ok = JudgeVerdictParser.TryParse(json, rubric, out _, out var error);

        ok.ShouldBeFalse();
        error.ShouldNotBeNull();
        error.ShouldContain("b");
    }

    [Fact(DisplayName = "Given a score outside [0, 1], when parsed, then TryParse returns false with a non-null error")]
    public void RejectOutOfRangeScore()
    {
        var rubric = MakeRubric(0.5);
        var json =
            """
            {
              "scores": [
                { "criterionId": "only", "score": 1.5, "rationale": "ok" }
              ],
              "overallScore": 0.5,
              "verdict": "pass",
              "notes": ""
            }
            """;

        var ok = JudgeVerdictParser.TryParse(json, rubric, out _, out var error);

        ok.ShouldBeFalse();
        error.ShouldNotBeNull();
        error.ShouldContain("[0, 1]");
    }

    [Fact(DisplayName = "Given malformed JSON, when parsed, then TryParse returns false without throwing")]
    public void RejectMalformedJson()
    {
        var rubric = MakeRubric(0.5);
        const string bad = "{ this is not valid JSON";

        var ok = JudgeVerdictParser.TryParse(bad, rubric, out _, out var error);

        ok.ShouldBeFalse();
        error.ShouldNotBeNull();
    }

    [Fact(DisplayName = "Given an empty string, when parsed, then TryParse returns false with an empty-message error")]
    public void RejectEmptyString()
    {
        var rubric = MakeRubric(0.5);

        var ok = JudgeVerdictParser.TryParse(string.Empty, rubric, out _, out var error);

        ok.ShouldBeFalse();
        error.ShouldNotBeNull();
    }

    private static EvalRubric MakeRubric(double minScore) =>
        new()
        {
            Criteria = [new EvalRubricCriterion { Id = "only", Description = "The only criterion.", Weight = 1.0 }],
            MinScore = minScore,
        };
}
