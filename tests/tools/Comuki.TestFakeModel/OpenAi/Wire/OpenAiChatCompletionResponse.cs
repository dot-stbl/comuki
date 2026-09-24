using System.Text.Json.Serialization;

namespace Comuki.TestFakeModel.OpenAi.Wire;

/// <summary>The non-streaming <c>POST /v1/chat/completions</c> response body.</summary>
public sealed record OpenAiChatCompletionResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("object")] string Object,
    [property: JsonPropertyName("created")] long Created,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("choices")] IReadOnlyList<OpenAiChoice> Choices,
    [property: JsonPropertyName("usage")] OpenAiUsage Usage);
