using Comuki.AgentEval.Corpus;

namespace Comuki.AgentEval.Judges;

/// <summary>What happened when <see cref="LlmJudge.EvaluateAsync"/> ran against a corpus entry.</summary>
public enum JudgeOutcomeKind
{
    /// <summary>No live env configured OR the entry has no rubric — judge was not attempted.</summary>
    Skipped,

    /// <summary>Judge ran and returned a parseable verdict.</summary>
    Scored,

    /// <summary>Judge ran but the verdict could not be parsed (or the call itself threw).</summary>
    Error,
}

/// <summary>
/// One judge's outcome on one corpus entry: kind + (verdict or
/// human-readable error message). Construct via the static factories
/// <see cref="Skipped"/>, <see cref="Scored"/>, <see cref="Error"/>.
/// </summary>
/// <param name="Kind">Which of the three outcomes this is.</param>
/// <param name="Verdict">Parsed judge verdict; non-null only when <see cref="Kind"/> is <see cref="JudgeOutcomeKind.Scored"/>.</param>
/// <param name="Message">Reason for a Skipped or Error outcome; null for Scored (the verdict itself carries the rationale).</param>
public sealed record JudgeOutcome(JudgeOutcomeKind Kind, JudgeVerdict? Verdict, string? Message)
{
    /// <summary>Constructs a <see cref="JudgeOutcomeKind.Skipped"/> outcome.</summary>
    public static JudgeOutcome Skipped(string reason) => new(JudgeOutcomeKind.Skipped, null, reason);

    /// <summary>Constructs a <see cref="JudgeOutcomeKind.Scored"/> outcome.</summary>
    public static JudgeOutcome Scored(JudgeVerdict verdict) => new(JudgeOutcomeKind.Scored, verdict, null);

    /// <summary>Constructs a <see cref="JudgeOutcomeKind.Error"/> outcome.</summary>
    public static JudgeOutcome Error(string message) => new(JudgeOutcomeKind.Error, null, message);
}

/// <summary>
/// Orchestrates one LLM-as-judge call per corpus entry: builds the
/// system+user prompt from the ticket / expected outcome / rubric / run
/// transcript, asks <see cref="ILlmJudgeClient"/> for a response, feeds
/// the raw text through <see cref="JudgeVerdictParser.TryParse"/>, and
/// packages the outcome as a <see cref="JudgeOutcome"/>. Never throws
/// past <see cref="EvaluateAsync"/> — any exception from the network
/// seam becomes an <see cref="JudgeOutcomeKind.Error"/> outcome.
/// </summary>
/// <remarks>
/// The prompt-building step is exposed as an internal method so a unit
/// test can assert the prompt's shape without making a network call.
/// </remarks>
public static class LlmJudge
{
    /// <summary>
    /// The system prompt — fixed across all calls of one run. Asks for
    /// JSON-only output matching the strict <see cref="JudgeVerdict"/>
    /// shape; nothing more. Per-criterion context (ticket, expected
    /// outcome, transcript) lives in the user prompt.
    /// </summary>
    public static string SystemPrompt => """
        You are a strict code-quality judge. You will be given:
          (1) a ticket the agent had to fix,
          (2) the expected outcome the user wanted,
          (3) the agent's tool-call transcript and final diff, and
          (4) a rubric with weighted criteria.
        Score each criterion in [0, 1] and emit STRICT JSON, no prose
        before or after the JSON. The shape MUST be:
          {
            "scores": [ { "criterionId": "<id>", "score": 0.0, "rationale": "<one short sentence>" } ],
            "overallScore": 0.0,
            "verdict": "pass" | "fail",
            "notes": "<one short sentence>"
          }
        The number of scores entries MUST equal the number of rubric
        criteria, with each entry's criterionId matching one rubric id
        exactly. Any deviation is a parse failure.
        """;

    /// <summary>
    /// Builds the per-entry user prompt. Internal so a unit test can
    /// pin the exact shape without a network call.
    /// </summary>
    /// <param name="entry">The corpus entry the judge is scoring.</param>
    /// <param name="transcriptSummary">A compact rendering of the agent's tool calls + final diff summary.</param>
    internal static string BuildUserPrompt(CorpusEntry entry, string transcriptSummary)
    {
        var ticketTitle = entry.Scenario.Ticket.Title;
        var ticketBody = entry.Scenario.Ticket.Body;
        var expectedOutcome = entry.Eval.ExpectedOutcome;
        var rubric = entry.Eval.Rubric;

        var rubricText = rubric is null
            ? "(no rubric)"
            : string.Join(
                "\n",
                rubric.Criteria.Select(criterion =>
                    $"- id={criterion.Id} weight={criterion.Weight:0.###} description=\"{criterion.Description}\""));

        return $$"""
            TICKET:
              title: {{ticketTitle}}
              body:
            {{ticketBody}}

            EXPECTED OUTCOME:
            {{expectedOutcome}}

            TRANSCRIPT:
            {{transcriptSummary}}

            RUBRIC (id, weight, description):
            {{rubricText}}

            Emit strict JSON only, matching the schema in your system prompt. Every rubric criterion must appear exactly once in `scores`, with its id verbatim. Score each criterion in [0, 1].
            """;
    }

    /// <summary>
    /// Builds the prompt, calls the judge, parses the verdict, returns
    /// a <see cref="JudgeOutcome"/>. <see cref="JudgeOutcomeKind.Skipped"/>
    /// when the client is null or the entry has no rubric; otherwise
    /// either <see cref="JudgeOutcomeKind.Scored"/> on a clean verdict
    /// or <see cref="JudgeOutcomeKind.Error"/> on a parse failure or
    /// any thrown exception from the client.
    /// </summary>
    public static async Task<JudgeOutcome> EvaluateAsync(
        ILlmJudgeClient? client,
        CorpusEntry entry,
        string transcriptSummary,
        CancellationToken ct)
    {
        if (client is null)
        {
            return JudgeOutcome.Skipped("no live judge env configured (COMUKI_LIVE_MODEL_BASE_URL not set)");
        }

        if (entry.Eval.Rubric is null)
        {
            return JudgeOutcome.Skipped("entry declares no eval.rubric — judge not attempted");
        }

        var systemPrompt = SystemPrompt;
        var userPrompt = BuildUserPrompt(entry, transcriptSummary);

        string rawResponse;
        try
        {
            rawResponse = await client.CompleteAsync(systemPrompt, userPrompt, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return JudgeOutcome.Error($"judge client threw: {exception.Message}");
        }

        return JudgeVerdictParser.TryParse(rawResponse, entry.Eval.Rubric, out var verdict, out var error)
            ? JudgeOutcome.Scored(verdict!)
            : JudgeOutcome.Error($"verdict parse failed: {error}");
    }
}
