using System.Text.Json;
using System.Text.Json.Serialization;

namespace Comuki.TestFakeModel.Anthropic;

/// <summary>Token usage as the Anthropic Messages API reports it (matches the shape <c>AnthropicUsageExtractor</c> reads).</summary>
public sealed record AnthropicUsage(
    [property: JsonPropertyName("input_tokens")] int InputTokens,
    [property: JsonPropertyName("output_tokens")] int OutputTokens);

/// <summary>
/// The non-streaming <c>POST /v1/messages</c> response body, and the
/// shape <c>message_start</c>'s <c>message</c> field reuses (with an
/// empty <see cref="Content"/> and a <c>null</c> <see cref="StopReason"/>
/// — the real API's own convention for that first event).
/// </summary>
public sealed record AnthropicMessageResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("content")] IReadOnlyList<AnthropicContentBlock> Content,
    [property: JsonPropertyName("stop_reason")] string? StopReason,
    [property: JsonPropertyName("stop_sequence")] string? StopSequence,
    [property: JsonPropertyName("usage")] AnthropicUsage Usage);

/// <summary>
/// One content block — <c>text</c> or <c>tool_use</c> — serialized with
/// only the fields that type needs (the rest stay <c>null</c> and are
/// dropped by <see cref="AnthropicJsonOptions.Default"/>'s null-ignore
/// setting).
/// </summary>
public sealed record AnthropicContentBlock(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string? Text = null,
    [property: JsonPropertyName("id")] string? Id = null,
    [property: JsonPropertyName("name")] string? Name = null,
    [property: JsonPropertyName("input")] JsonElement? Input = null);

/// <summary>SSE payload for the <c>message_start</c> event.</summary>
public sealed record MessageStartEvent(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("message")] AnthropicMessageResponse Message);

/// <summary>SSE payload for the <c>content_block_start</c> event.</summary>
public sealed record ContentBlockStartEvent(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("content_block")] AnthropicContentBlock ContentBlock);

/// <summary>A delta payload — <c>text_delta</c> (<see cref="Text"/>) or <c>input_json_delta</c> (<see cref="PartialJson"/>).</summary>
public sealed record AnthropicDelta(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string? Text = null,
    [property: JsonPropertyName("partial_json")] string? PartialJson = null);

/// <summary>SSE payload for the <c>content_block_delta</c> event.</summary>
public sealed record ContentBlockDeltaEvent(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("delta")] AnthropicDelta Delta);

/// <summary>SSE payload for the <c>content_block_stop</c> event.</summary>
public sealed record ContentBlockStopEvent(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("index")] int Index);

/// <summary>The <c>delta</c> field of a <c>message_delta</c> event — the final stop reason.</summary>
public sealed record AnthropicMessageDelta(
    [property: JsonPropertyName("stop_reason")] string? StopReason,
    [property: JsonPropertyName("stop_sequence")] string? StopSequence);

/// <summary>The <c>usage</c> field of a <c>message_delta</c> event — cumulative output tokens only (the real API's shape).</summary>
public sealed record AnthropicMessageDeltaUsage([property: JsonPropertyName("output_tokens")] int OutputTokens);

/// <summary>SSE payload for the <c>message_delta</c> event.</summary>
public sealed record MessageDeltaEvent(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("delta")] AnthropicMessageDelta Delta,
    [property: JsonPropertyName("usage")] AnthropicMessageDeltaUsage Usage);

/// <summary>SSE payload for the terminal <c>message_stop</c> event.</summary>
public sealed record MessageStopEvent([property: JsonPropertyName("type")] string Type);
