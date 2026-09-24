using System.Text.Json.Serialization;

namespace Comuki.TestFakeModel.OpenAi.Wire.Message;

/// <summary>
/// The non-streaming response's <c>choices[].message</c> object.
/// <see cref="Content"/> is the concatenation of the scripted entry's text
/// blocks (<c>null</c> when the entry is tool-calls-only — the real API's
/// own convention for a tool-only assistant turn); <see cref="ToolCalls"/>
/// is <c>null</c> when the entry has no <c>tool_use</c> blocks.
/// </summary>
public sealed record OpenAiChatMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string? Content,
    [property: JsonPropertyName("tool_calls")] IReadOnlyList<OpenAiToolCall>? ToolCalls);
