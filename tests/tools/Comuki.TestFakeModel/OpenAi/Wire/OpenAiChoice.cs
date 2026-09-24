using System.Text.Json.Serialization;
using Comuki.TestFakeModel.OpenAi.Wire.Message;

namespace Comuki.TestFakeModel.OpenAi.Wire;

/// <summary>One entry of the non-streaming response's <c>choices</c> array — the fake only ever scripts a single choice (index 0).</summary>
public sealed record OpenAiChoice(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("message")] OpenAiChatMessage Message,
    [property: JsonPropertyName("finish_reason")] string? FinishReason);
