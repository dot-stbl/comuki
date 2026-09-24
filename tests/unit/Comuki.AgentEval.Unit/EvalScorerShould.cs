using Comuki.AgentEval.Corpus;
using Comuki.AgentEval.Judges;
using Comuki.AgentEval.Scoring;
using Comuki.AgentTest.Runner.Scenarios;
using Shouldly;
using Xunit;

namespace Comuki.AgentEval.Unit;

/// <summary>
/// Exercises every branch of <see cref="EvalScorer.Score"/>: the
/// deterministic gate, the rubric+judge branches, the no-rubric
/// branch, the declared-rubric-with-judge-skipped branch, and the
/// declared-rubric-with-judge-error branch.
/// </summary>
public sealed class EvalScorerShould
{
    [Fact(DisplayName = "Given all-deterministic-pass and no rubric, when scored, then Passed = true and QualityScore = 1.0")]
    public void AllDeterministicPassNoRubric()
    {
        var entry = MakeEntry(rubric: null);
        var verdicts = new[]
        {
            MakeVerdict("diff-applies", true),
            MakeVerdict("tool-call-count", null),
        };

        var score = EvalScorer.Score(entry, verdicts, JudgeOutcome.Skipped("not attempted"));

        score.Passed.ShouldBeTrue();
        score.DeterministicPass.ShouldBeTrue();
        score.QualityScore.ShouldBe(1d);
        score.Deterministic.Count.ShouldBe(2);
    }

    [Fact(DisplayName = "Given one deterministic fail, when scored, then Passed = false (deterministic gate overrides judge regardless of judge outcome)")]
    public void OneDeterministicFailOverridesEverything()
    {
        var entry = MakeEntry(rubric: MakeRubric(minScore: 0.5));
        var verdicts = new[]
        {
            MakeVerdict("diff-applies", true),
            MakeVerdict("tests-pass", false),
        };
        var judgeVerdict = MakeJudgeVerdict(1.0);

        var score = EvalScorer.Score(entry, verdicts, JudgeOutcome.Scored(judgeVerdict));

        score.Passed.ShouldBeFalse();
        // DeterministicPass=false means the deterministic-part of the
        // QualityScore formula contributes 0, even though the judge
        // scored 1.0 — the deterministic gate caps the judge-side at 0.5.
        score.QualityScore.ShouldBe(0.5);
    }

    [Fact(DisplayName = "Given deterministic pass and a judge that scored above MinScore, when scored, then Passed = true")]
    public void JudgeScoredAboveMinScore()
    {
        var entry = MakeEntry(rubric: MakeRubric(minScore: 0.5));
        var verdicts = new[] { MakeVerdict("diff-applies", true) };
        var judgeVerdict = MakeJudgeVerdict(0.9);

        var score = EvalScorer.Score(entry, verdicts, JudgeOutcome.Scored(judgeVerdict));

        score.Passed.ShouldBeTrue();
        score.QualityScore.ShouldBe(1d * 0.5 + 0.9 * 0.5);
    }

    [Fact(DisplayName = "Given deterministic pass and a judge that scored below MinScore, when scored, then Passed = false")]
    public void JudgeScoredBelowMinScore()
    {
        var entry = MakeEntry(rubric: MakeRubric(minScore: 0.8));
        var verdicts = new[] { MakeVerdict("diff-applies", true) };
        var judgeVerdict = MakeJudgeVerdict(0.4);

        var score = EvalScorer.Score(entry, verdicts, JudgeOutcome.Scored(judgeVerdict));

        score.Passed.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given deterministic pass and a rubric declared with a judge Error outcome, when scored, then Passed = false (mandatory rubric that failed is a failure)")]
    public void RubricDeclaredAndJudgeErrored()
    {
        var entry = MakeEntry(rubric: MakeRubric(minScore: 0.5));
        var verdicts = new[] { MakeVerdict("diff-applies", true) };

        var score = EvalScorer.Score(entry, verdicts, JudgeOutcome.Error("judge threw"));

        score.Passed.ShouldBeFalse();
        score.Judge.Kind.ShouldBe(JudgeOutcomeKind.Error);
    }

    [Fact(DisplayName = "Given deterministic pass and a rubric declared with judge Skipped (no live env), when scored, then Passed = DeterministicPass")]
    public void RubricDeclaredAndJudgeSkipped()
    {
        var entry = MakeEntry(rubric: MakeRubric(minScore: 0.9));
        var verdicts = new[] { MakeVerdict("diff-applies", true) };

        var score = EvalScorer.Score(entry, verdicts, JudgeOutcome.Skipped("no live env"));

        score.Passed.ShouldBeTrue();
        score.Judge.Kind.ShouldBe(JudgeOutcomeKind.Skipped);
    }

    [Fact(DisplayName = "Given a judge verdict whose OverallScore disagrees with the recomputed per-criterion average, when parsed, then OverallScore is the recomputed value (not the model's self-reported one)")]
    public void RecomputedOverallScoreWins()
    {
        var rubric = MakeRubric(minScore: 0.5);
        var rawJson =
            /*lang=json,strict*/
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
        var ok = JudgeVerdictParser.TryParse(rawJson, rubric, out var verdict, out _);
        ok.ShouldBeTrue();
        verdict!.OverallScore.ShouldBe(1d);
    }

    private static CorpusEntry MakeEntry(EvalRubric? rubric)
    {
        return new CorpusEntry(
            new ScenarioDefinition
            {
                SchemaVersion = 1,
                Name = "test-entry",
                Description = "test",
                Ticket = new ScenarioTicket { Title = "t", Body = "b" },
                Worker = new ScenarioWorker { Image = "comuki-agent-test-worker:ws6" },
            },
            new EvalExtension { Rubric = rubric },
            "/tmp/test.scenario.yaml");
    }

    private static EvalRubric MakeRubric(double minScore)
    {
        return new EvalRubric
        {
            Criteria = [new EvalRubricCriterion { Id = "only", Description = "The only criterion.", Weight = 1.0 }],
            MinScore = minScore,
        };
    }

    private static DeterministicVerdict MakeVerdict(string name, bool? passed)
    {
        return new DeterministicVerdict { JudgeName = name, Passed = passed, Message = $"message for {name}" };
    }

    private static JudgeVerdict MakeJudgeVerdict(double overallScore)
    {
        return new JudgeVerdict(
            [new JudgeCriterionScore("only", 1.0, "perfect")],
            overallScore,
            "pass",
            string.Empty);
    }
}
