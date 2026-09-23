using System.Text.Json.Serialization;

namespace Comuki.TestFakeModel.OpenAi.Wire;

/// <summary>Token usage as the OpenAI Chat Completions API reports it (matches the shape <c>OpenAiUsageExtractor</c> reads).</summary>
public sealed record OpenAiUsage(
    [property: JsonPropertyName("prompt_tokens")] int PromptTokens,
    [property: JsonPropertyName("completion_tokens")] int CompletionTokens,
    [property: JsonPropertyName("total_tokens")] int TotalTokens);
