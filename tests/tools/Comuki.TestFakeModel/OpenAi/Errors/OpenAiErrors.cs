namespace Comuki.TestFakeModel.OpenAi.Errors;

/// <summary>OpenAI-shaped <c>{"error":{...}}</c> error bodies for the endpoint's failure paths — mirrors <c>Anthropic.Errors.AnthropicErrors</c>.</summary>
public static class OpenAiErrors
{
    /// <summary>A malformed/unparseable request body — <c>invalid_request_error</c>, HTTP 400.</summary>
    public static OpenAiErrorBody InvalidRequest(string message)
    {
        return new OpenAiErrorBody(new OpenAiErrorDetail(message, "invalid_request_error", Param: null, Code: null));
    }

    /// <summary>
    /// A fakeScript resolution failure (exhausted or a match mismatch), or —
    /// reused by <c>Cassettes</c> replay mode — a cassette exhaustion/mismatch
    /// against the next expected exchange. <c>api_error</c>, HTTP 500.
    /// </summary>
    public static OpenAiErrorBody ScriptFailure(string message)
    {
        return new OpenAiErrorBody(new OpenAiErrorDetail(message, "api_error", Param: null, Code: null));
    }
}
