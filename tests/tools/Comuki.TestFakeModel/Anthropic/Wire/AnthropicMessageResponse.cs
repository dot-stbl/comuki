using System.Text.Json.Serialization;

namespace Comuki.TestFakeModel.Anthropic.Wire;

/// <summary>
/// The non-streaming <c>POST /v1/messages</c> response body, and the
/// shape <c>message_start</c>'s <c>message</c> field reuses (with an
/// empty <see cref="Content"/> and a <c>null</c> <see cref="StopReason"/>
/// — the real API's own convention for that first event, so both fields
/// are legitimately nullable/empty rather than something to hide).
/// <see cref="Content"/> holds a mix of <see cref="Blocks.AnthropicTextBlock"/>
/// and <see cref="Blocks.AnthropicToolUseBlock"/> — each block serializes by its
/// own runtime type, which is why the element type is <c>object</c>
/// rather than a shared nullable-everything record.
/// </summary>
public sealed record AnthropicMessageResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("content")] IReadOnlyList<object> Content,
    [property: JsonPropertyName("stop_reason")] string? StopReason,
    [property: JsonPropertyName("stop_sequence")] string? StopSequence,
    [property: JsonPropertyName("usage")] AnthropicUsage Usage);
