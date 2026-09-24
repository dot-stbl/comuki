namespace Comuki.TestFakeModel.Anthropic.Errors;

/// <summary>Anthropic-shaped <c>{"type":"error","error":{...}}</c> error bodies for the endpoint's failure paths.</summary>
public static class AnthropicErrors
{
    /// <summary>A malformed/unparseable request body — <c>invalid_request_error</c>, HTTP 400.</summary>
    public static AnthropicErrorBody InvalidRequest(string message)
    {
        return new AnthropicErrorBody("error", new AnthropicErrorDetail("invalid_request_error", message));
    }

    /// <summary>A fakeScript resolution failure (exhausted or a match mismatch) — <c>api_error</c>, HTTP 500.</summary>
    public static AnthropicErrorBody ScriptFailure(string message)
    {
        return new AnthropicErrorBody("error", new AnthropicErrorDetail("api_error", message));
    }
}
