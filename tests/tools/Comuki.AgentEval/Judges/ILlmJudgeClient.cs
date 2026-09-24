namespace Comuki.AgentEval.Judges;

/// <summary>
/// The single network seam the LLM-as-judge uses. Production code
/// constructs an <c>HapyLlmJudgeClient</c>; unit tests inject a fake
/// implementation that returns canned responses or throws to exercise
/// the <c>Error</c> branch of <see cref="JudgeOutcomeKind"/>.
/// </summary>
public interface ILlmJudgeClient : IAsyncDisposable
{
    /// <summary>
    /// Sends a judge prompt and returns the model's raw response —
    /// exactly the text the judge model produced, with no JSON parsing or
    /// cleanup applied at this seam. The caller is responsible for
    /// passing the result through <see cref="JudgeVerdictParser.TryParse"/>.
    /// </summary>
    /// <param name="systemPrompt">The system's role/context — stays fixed across all calls of one rubric evaluation.</param>
    /// <param name="userPrompt">The per-entry user message containing the ticket, expected outcome, and transcript summary.</param>
    /// <param name="ct">Cancellation forwarded from the caller's scope.</param>
    public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct);
}
