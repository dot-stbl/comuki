using System.Text.Json;
using Comuki.TestFakeModel.Determinism;
using Comuki.TestFakeModel.Scripting;

namespace Comuki.TestFakeModel.Anthropic;

/// <summary>
/// Writes the full <c>stream: true</c> SSE event sequence for one
/// scripted response — <c>message_start</c>, a
/// <c>content_block_start</c>/delta.../<c>content_block_stop</c> run per
/// content block (<c>text_delta</c> for text, <c>input_json_delta</c> for
/// <c>tool_use</c> input), then <c>message_delta</c> and
/// <c>message_stop</c> — matching the shapes in the Anthropic streaming
/// docs exactly.
/// </summary>
internal sealed class AnthropicSseWriter(HttpResponse response)
{
    /// <summary>Writes every event for <paramref name="scripted"/> to the response, flushing after each one.</summary>
    public async Task WriteStreamAsync(
        string messageId,
        string model,
        string scenarioName,
        int requestIndex,
        FakeScriptResponse scripted,
        CancellationToken cancellationToken)
    {
        var (inputTokens, outputTokens) = AnthropicResponseFactory.ResolveUsage(scripted.Usage);

        await WriteEventAsync(
            "message_start",
            new MessageStartEvent(
                "message_start",
                new AnthropicMessageResponse(messageId, "message", "assistant", model, [], null, null, new AnthropicUsage(inputTokens, 0))),
            cancellationToken);

        for (var index = 0; index < scripted.Content.Count; index++)
        {
            await WriteContentBlockAsync(scripted.Content[index], index, scenarioName, requestIndex, cancellationToken);
        }

        await WriteEventAsync(
            "message_delta",
            new MessageDeltaEvent("message_delta", new AnthropicMessageDelta(scripted.StopReason, null), new AnthropicMessageDeltaUsage(outputTokens)),
            cancellationToken);

        await WriteEventAsync("message_stop", new MessageStopEvent("message_stop"), cancellationToken);
    }

    private async Task WriteContentBlockAsync(
        FakeContentBlock block,
        int index,
        string scenarioName,
        int requestIndex,
        CancellationToken cancellationToken)
    {
        switch (block)
        {
            case FakeContentBlock.TextBlock text:
                await WriteTextBlockAsync(text, index, cancellationToken);
                break;
            case FakeContentBlock.ToolUseBlock toolUse:
                await WriteToolUseBlockAsync(toolUse, index, scenarioName, requestIndex, cancellationToken);
                break;
            default:
                throw new NotSupportedException($"unsupported fake content block type: {block.GetType()}");
        }

        await WriteEventAsync("content_block_stop", new ContentBlockStopEvent("content_block_stop", index), cancellationToken);
    }

    private async Task WriteTextBlockAsync(FakeContentBlock.TextBlock text, int index, CancellationToken cancellationToken)
    {
        await WriteEventAsync(
            "content_block_start",
            new ContentBlockStartEvent("content_block_start", index, new AnthropicContentBlock("text", Text: string.Empty)),
            cancellationToken);

        foreach (var chunk in TextChunker.Chunk(text.Text, TextChunker.DefaultChunkSize))
        {
            await WriteEventAsync(
                "content_block_delta",
                new ContentBlockDeltaEvent("content_block_delta", index, new AnthropicDelta("text_delta", Text: chunk)),
                cancellationToken);
        }
    }

    private async Task WriteToolUseBlockAsync(
        FakeContentBlock.ToolUseBlock toolUse,
        int index,
        string scenarioName,
        int requestIndex,
        CancellationToken cancellationToken)
    {
        var toolUseId = DeterministicIds.ToolUseId(scenarioName, requestIndex, index);
        await WriteEventAsync(
            "content_block_start",
            new ContentBlockStartEvent(
                "content_block_start",
                index,
                new AnthropicContentBlock("tool_use", Id: toolUseId, Name: toolUse.Name, Input: DynamicJsonConverter.EmptyObject)),
            cancellationToken);

        var partialJson = JsonSerializer.Serialize(toolUse.Input, AnthropicJsonOptions.Default);
        foreach (var chunk in TextChunker.Chunk(partialJson, TextChunker.DefaultChunkSize))
        {
            await WriteEventAsync(
                "content_block_delta",
                new ContentBlockDeltaEvent("content_block_delta", index, new AnthropicDelta("input_json_delta", PartialJson: chunk)),
                cancellationToken);
        }
    }

    private async Task WriteEventAsync<TPayload>(string eventType, TPayload payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, AnthropicJsonOptions.Default);
        await response.WriteAsync($"event: {eventType}\n", cancellationToken);
        await response.WriteAsync($"data: {json}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }
}
