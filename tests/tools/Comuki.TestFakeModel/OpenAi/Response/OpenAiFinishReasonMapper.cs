namespace Comuki.TestFakeModel.OpenAi.Response;

/// <summary>
/// Maps a scripted <c>FakeScriptResponse.StopReason</c> — written in
/// Anthropic's vocabulary (<c>end_turn</c>/<c>tool_use</c>/<c>max_tokens</c>/...)
/// since that's the one vocabulary <c>Scripting.Loading.FakeScriptMapper</c>
/// infers — onto OpenAI's <c>finish_reason</c> vocabulary. One scripted
/// entry serves both wire shapes unchanged; only the label differs.
/// </summary>
public static class OpenAiFinishReasonMapper
{
    /// <summary>Maps <paramref name="stopReason"/> onto an OpenAI <c>finish_reason</c> value.</summary>
    public static string Map(string stopReason)
    {
        return stopReason switch
        {
            "tool_use" => "tool_calls",
            "max_tokens" => "length",
            _ => "stop",
        };
    }
}
