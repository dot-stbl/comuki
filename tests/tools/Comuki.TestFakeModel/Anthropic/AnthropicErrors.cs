using System.Text.Json.Serialization;

namespace Comuki.TestFakeModel.Anthropic;

/// <summary>Anthropic-shaped <c>{"type":"error","error":{...}}</c> error bodies for the endpoint's failure paths.</summary>
internal static class AnthropicErrors
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

/// <summary>The Anthropic error envelope.</summary>
public sealed record AnthropicErrorBody(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("error")] AnthropicErrorDetail Error);

/// <summary>The nested <c>error</c> object.</summary>
public sealed record AnthropicErrorDetail(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("message")] string Message);
