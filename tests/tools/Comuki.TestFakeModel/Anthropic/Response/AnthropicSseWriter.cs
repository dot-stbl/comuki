using System.Text.Json;
using System.Text.Json.Serialization;
using Comuki.TestFakeModel.Anthropic.Wire;
using Comuki.TestFakeModel.Anthropic.Wire.Blocks;
using Comuki.TestFakeModel.Determinism;
using Comuki.TestFakeModel.Scripting.Loading;
using Comuki.TestFakeModel.Scripting.Model.Response;

namespace Comuki.TestFakeModel.Anthropic.Response;

/// <summary>
/// Writes the full <c>stream: true</c> SSE event sequence for one
/// scripted response — <c>message_start</c>, a
/// <c>content_block_start</c>/delta.../<c>content_block_stop</c> run per
/// content block (<c>text_delta</c> for text, <c>input_json_delta</c> for
/// <c>tool_use</c> input), then <c>message_delta</c> and
/// <c>message_stop</c> — matching the shapes in the Anthropic streaming
/// docs exactly. The per-event payload records below are declared
/// <c>file</c>-scoped: every one of them is constructed and serialized
/// only here, so none needs its own file under folder-organization.md §1.
/// </summary>
public static class AnthropicSseWriter
{
    /// <summary>Writes every event for <paramref name="scripted"/> to <paramref name="response"/>, flushing after each one.</summary>
    public static async Task WriteStreamAsync(
        HttpResponse response,
        string messageId,
        string model,
        string scenarioName,
        int requestIndex,
        FakeScriptResponse scripted,
        CancellationToken cancellationToken)
    {
        var usage = AnthropicResponseFactory.ResolveUsage(scripted.Usage);

        await AnthropicSseEventWriter.WriteEventAsync(
            response,
            "message_start",
            new MessageStartEvent("message_start", new AnthropicMessageResponse(messageId, "message", "assistant", model, [], null, null, new AnthropicUsage(usage.InputTokens, 0))),
            cancellationToken);

        for (var index = 0; index < scripted.Content.Count; index++)
        {
            await AnthropicSseEventWriter.WriteContentBlockAsync(response, scripted.Content[index], index, scenarioName, requestIndex, cancellationToken);
        }

        await AnthropicSseEventWriter.WriteEventAsync(
            response,
            "message_delta",
            new MessageDeltaEvent("message_delta", new AnthropicMessageDelta(scripted.StopReason, null), new AnthropicMessageDeltaUsage(usage.OutputTokens)),
            cancellationToken);

        await AnthropicSseEventWriter.WriteEventAsync(response, "message_stop", new MessageStopEvent("message_stop"), cancellationToken);
    }
}

/// <summary>The per-event write steps <see cref="AnthropicSseWriter"/> composes — extracted per class-layout-and-tooling.md §1a.</summary>
file static class AnthropicSseEventWriter
{
    public static async Task WriteContentBlockAsync(
        HttpResponse response,
        FakeContentBlock block,
        int index,
        string scenarioName,
        int requestIndex,
        CancellationToken cancellationToken)
    {
        switch (block)
        {
            case FakeContentBlock.TextBlock text:
                await WriteTextBlockAsync(response, text, index, cancellationToken);
                break;
            case FakeContentBlock.ToolUseBlock toolUse:
                await WriteToolUseBlockAsync(response, toolUse, index, scenarioName, requestIndex, cancellationToken);
                break;
            default:
                throw new NotSupportedException($"unsupported fake content block type: {block.GetType()}");
        }

        await WriteEventAsync(response, "content_block_stop", new ContentBlockStopEvent("content_block_stop", index), cancellationToken);
    }

    public static async Task WriteTextBlockAsync(HttpResponse response, FakeContentBlock.TextBlock text, int index, CancellationToken cancellationToken)
    {
        await WriteEventAsync(response, "content_block_start", new ContentBlockStartEvent("content_block_start", index, new AnthropicTextBlock("text", string.Empty)), cancellationToken);

        foreach (var chunk in TextChunker.Chunk(text.Text, TextChunker.DefaultChunkSize))
        {
            await WriteEventAsync(response, "content_block_delta", new ContentBlockDeltaEvent("content_block_delta", index, new AnthropicTextDelta("text_delta", chunk)), cancellationToken);
        }
    }

    public static async Task WriteToolUseBlockAsync(
        HttpResponse response,
        FakeContentBlock.ToolUseBlock toolUse,
        int index,
        string scenarioName,
        int requestIndex,
        CancellationToken cancellationToken)
    {
        var toolUseId = DeterministicIds.ToolUseId(scenarioName, requestIndex, index);
        await WriteEventAsync(
            response,
            "content_block_start",
            new ContentBlockStartEvent("content_block_start", index, new AnthropicToolUseBlock("tool_use", toolUseId, toolUse.Name, DynamicJsonConverter.EmptyObject)),
            cancellationToken);

        foreach (var chunk in TextChunker.Chunk(JsonSerializer.Serialize(toolUse.Input, JsonSerializerOptions.Web), TextChunker.DefaultChunkSize))
        {
            await WriteEventAsync(response, "content_block_delta", new ContentBlockDeltaEvent("content_block_delta", index, new AnthropicInputJsonDelta("input_json_delta", chunk)), cancellationToken);
        }
    }

    public static async Task WriteEventAsync<TPayload>(HttpResponse response, string eventType, TPayload payload, CancellationToken cancellationToken)
    {
        await response.WriteAsync($"event: {eventType}\n", cancellationToken);
        await response.WriteAsync($"data: {JsonSerializer.Serialize(payload, JsonSerializerOptions.Web)}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }
}

/// <summary>SSE payload for the <c>message_start</c> event.</summary>
file sealed record MessageStartEvent(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("message")] AnthropicMessageResponse Message);

/// <summary>SSE payload for the <c>content_block_start</c> event; <see cref="ContentBlock"/> is an <see cref="AnthropicTextBlock"/> or <see cref="AnthropicToolUseBlock"/>.</summary>
file sealed record ContentBlockStartEvent(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("content_block")] object ContentBlock);

/// <summary>The <c>delta</c> payload for a <c>text_delta</c> content_block_delta event.</summary>
file sealed record AnthropicTextDelta(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string Text);

/// <summary>The <c>delta</c> payload for an <c>input_json_delta</c> content_block_delta event.</summary>
file sealed record AnthropicInputJsonDelta(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("partial_json")] string PartialJson);

/// <summary>SSE payload for the <c>content_block_delta</c> event; <see cref="Delta"/> is an <see cref="AnthropicTextDelta"/> or <see cref="AnthropicInputJsonDelta"/>.</summary>
file sealed record ContentBlockDeltaEvent(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("delta")] object Delta);

/// <summary>SSE payload for the <c>content_block_stop</c> event.</summary>
file sealed record ContentBlockStopEvent(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("index")] int Index);

/// <summary>The <c>delta</c> field of a <c>message_delta</c> event — the final stop reason (legitimately nullable — the real API serializes it as literal <c>null</c>, not omitted).</summary>
file sealed record AnthropicMessageDelta(
    [property: JsonPropertyName("stop_reason")] string? StopReason,
    [property: JsonPropertyName("stop_sequence")] string? StopSequence);

/// <summary>The <c>usage</c> field of a <c>message_delta</c> event — cumulative output tokens only (the real API's shape).</summary>
file sealed record AnthropicMessageDeltaUsage([property: JsonPropertyName("output_tokens")] int OutputTokens);

/// <summary>SSE payload for the <c>message_delta</c> event.</summary>
file sealed record MessageDeltaEvent(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("delta")] AnthropicMessageDelta Delta,
    [property: JsonPropertyName("usage")] AnthropicMessageDeltaUsage Usage);

/// <summary>SSE payload for the terminal <c>message_stop</c> event.</summary>
file sealed record MessageStopEvent([property: JsonPropertyName("type")] string Type);
