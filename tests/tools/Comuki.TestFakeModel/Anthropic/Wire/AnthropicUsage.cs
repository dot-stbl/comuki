using System.Text.Json.Serialization;

namespace Comuki.TestFakeModel.Anthropic.Wire;

/// <summary>Token usage as the Anthropic Messages API reports it (matches the shape <c>AnthropicUsageExtractor</c> reads).</summary>
public sealed record AnthropicUsage(
    [property: JsonPropertyName("input_tokens")] int InputTokens,
    [property: JsonPropertyName("output_tokens")] int OutputTokens);
