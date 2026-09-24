using System.Text.Json.Serialization;

namespace Comuki.TestFakeModel.Anthropic.Wire.Blocks;

/// <summary>
/// A <c>text</c> content block — the full shape for the non-streaming
/// response and the (empty-text) shape <c>content_block_start</c> opens
/// with before <c>text_delta</c> events fill it in.
/// </summary>
public sealed record AnthropicTextBlock(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string Text);
