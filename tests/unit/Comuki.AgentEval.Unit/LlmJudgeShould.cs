using Comuki.AgentEval.Corpus;
using Comuki.AgentEval.Judges;
using Comuki.AgentTest.Runner.Scenarios;
using Shouldly;
using Xunit;

namespace Comuki.AgentEval.Unit;

/// <summary>
/// Exercises <see cref="LlmJudge.EvaluateAsync"/>'s three outcome
/// kinds via a <see cref="FakeLlmJudgeClient"/>: <see cref="JudgeOutcomeKind.Skipped"/>
/// when the client is null or the entry has no rubric; <see cref="JudgeOutcomeKind.Scored"/>
/// when the fake client returns strict-schema JSON; <see cref="JudgeOutcomeKind.Error"/>
/// when the fake client throws, or returns malformed JSON the parser
/// rejects. Never touches a real network.
/// </summary>
public sealed class LlmJudgeShould
{
    [Fact(DisplayName = "Given client is null, when evaluated, then the outcome is Skipped with a no-live-env message")]
    public async Task SkippedWhenClientIsNullAsync()
    {
        var entry = MakeEntry(rubric: MakeRubric(minScore: 0.5));

        var outcome = await LlmJudge.EvaluateAsync(client: null, entry, transcriptSummary: "summary", TestContext.Current.CancellationToken);

        outcome.Kind.ShouldBe(JudgeOutcomeKind.Skipped);
        (outcome.Message ?? "").ShouldContain("no live judge env");
        outcome.Verdict.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a client but no rubric declared, when evaluated, then the outcome is Skipped")]
    public async Task SkippedWhenRubricMissingAsync()
    {
        var client = new FakeLlmJudgeClient("ignored — should never be called");
        var entry = MakeEntry(rubric: null);

        var outcome = await LlmJudge.EvaluateAsync(client, entry, transcriptSummary: "summary", TestContext.Current.CancellationToken);

        outcome.Kind.ShouldBe(JudgeOutcomeKind.Skipped);
        (outcome.Message ?? "").ShouldContain("no eval.rubric");
        client.CallCount.ShouldBe(0);
    }

    [Fact(DisplayName = "Given a fake client returning valid strict-schema JSON, when evaluated, then the outcome is Scored with the recomputed OverallScore")]
    public async Task ScoredOnValidJsonAsync()
    {
        var rubric = MakeRubric(minScore: 0.5);
        var client = new FakeLlmJudgeClient(
            """
            {
              "scores": [
                { "criterionId": "only", "score": 1.0, "rationale": "perfect" }
              ],
              "overallScore": 0.99,
              "verdict": "pass",
              "notes": "fine"
            }
            """);
        var entry = MakeEntry(rubric);

        var outcome = await LlmJudge.EvaluateAsync(client, entry, transcriptSummary: "summary", TestContext.Current.CancellationToken);

        outcome.Kind.ShouldBe(JudgeOutcomeKind.Scored);
        outcome.Verdict.ShouldNotBeNull();
        outcome.Verdict!.OverallScore.ShouldBe(1d);
        outcome.Verdict.Verdict.ShouldBe("pass");
        client.CallCount.ShouldBe(1);
    }

    [Fact(DisplayName = "Given a fake client that throws, when evaluated, then the outcome is Error with the thrown message")]
    public async Task ErrorWhenClientThrowsAsync()
    {
        var client = new FakeLlmJudgeClient(new InvalidOperationException("network down"));
        var entry = MakeEntry(rubric: MakeRubric(minScore: 0.5));

        var outcome = await LlmJudge.EvaluateAsync(client, entry, transcriptSummary: "summary", TestContext.Current.CancellationToken);

        outcome.Kind.ShouldBe(JudgeOutcomeKind.Error);
        (outcome.Message ?? "").ShouldContain("network down");
        outcome.Verdict.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a fake client returning malformed JSON, when evaluated, then the outcome is Error with a parse-failure message")]
    public async Task ErrorOnMalformedJsonAsync()
    {
        var client = new FakeLlmJudgeClient("{ this is not valid JSON");
        var entry = MakeEntry(rubric: MakeRubric(minScore: 0.5));

        var outcome = await LlmJudge.EvaluateAsync(client, entry, transcriptSummary: "summary", TestContext.Current.CancellationToken);

        outcome.Kind.ShouldBe(JudgeOutcomeKind.Error);
        (outcome.Message ?? "").ShouldContain("verdict parse failed");
        outcome.Verdict.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a fake client returning JSON missing a declared criterion, when evaluated, then the outcome is Error (not silently ignored)")]
    public async Task ErrorOnMissingCriterionIdAsync()
    {
        var client = new FakeLlmJudgeClient(
            """
            {
              "scores": [
                { "criterionId": "different", "score": 1.0, "rationale": "ok" }
              ],
              "overallScore": 0.5,
              "verdict": "pass",
              "notes": ""
            }
            """);
        var entry = MakeEntry(rubric: MakeRubric(minScore: 0.5));

        var outcome = await LlmJudge.EvaluateAsync(client, entry, transcriptSummary: "summary", TestContext.Current.CancellationToken);

        outcome.Kind.ShouldBe(JudgeOutcomeKind.Error);
        (outcome.Message ?? "").ShouldContain("different");
    }

    private static CorpusEntry MakeEntry(EvalRubric? rubric) =>
        new(
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

    private static EvalRubric MakeRubric(double minScore) =>
        new()
        {
            Criteria = [new EvalRubricCriterion { Id = "only", Description = "The only criterion.", Weight = 1.0 }],
            MinScore = minScore,
        };
}
