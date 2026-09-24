using System.Text.Json.Serialization;

namespace Comuki.TestFakeModel.OpenAi.Wire.Message;

/// <summary>One entry of the non-streaming response's <c>message.tool_calls</c> array.</summary>
public sealed record OpenAiToolCall(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("function")] OpenAiToolCallFunction Function);
