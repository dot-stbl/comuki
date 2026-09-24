using System.Text.Json;
using System.Text.Json.Serialization;

namespace Comuki.TestFakeModel.Anthropic.Wire.Blocks;

/// <summary>
/// A <c>tool_use</c> content block — the full shape for the non-streaming
/// response and the (empty-<see cref="Input"/>) shape
/// <c>content_block_start</c> opens with before <c>input_json_delta</c>
/// events fill it in.
/// </summary>
public sealed record AnthropicToolUseBlock(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("input")] JsonElement Input);
