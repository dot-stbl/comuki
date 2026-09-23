using System.Text.Json.Serialization;

namespace Comuki.TestFakeModel.OpenAi.Wire.Message;

/// <summary>
/// A <c>tool_calls[].function</c> object on the non-streaming response.
/// Unlike Anthropic's <c>tool_use.input</c> (a JSON object),
/// <see cref="Arguments"/> is a JSON-encoded <em>string</em> — the wire
/// convention the real API uses. The streaming-chunk equivalent (where
/// <c>name</c>/<c>arguments</c> may each be absent on a continuation
/// delta) is a distinct, file-scoped shape in <c>Response/OpenAiSseWriter.cs</c>
/// — the two shapes diverge enough (an accumulating partial string vs. a
/// complete one) that sharing a type would make one of them lie about
/// what's actually on the wire.
/// </summary>
public sealed record OpenAiToolCallFunction(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("arguments")] string Arguments);
