using System.Text.Json;
using System.Text.Json.Serialization;
using Comuki.TestFakeModel.Anthropic.Response;
using Comuki.TestFakeModel.Determinism;
using Comuki.TestFakeModel.OpenAi.Wire;
using Comuki.TestFakeModel.Scripting.Model.Response;

namespace Comuki.TestFakeModel.OpenAi.Response;

/// <summary>
/// Writes the full <c>stream: true</c> SSE event sequence for one scripted
/// response in OpenAI's <c>chat.completion.chunk</c> shape: a role-only
/// opening chunk, a content/tool_calls delta per scripted content block, a
/// closing chunk carrying <c>finish_reason</c>, a trailing usage-only chunk
/// (mirrors requesting <c>stream_options: {"include_usage": true}</c> —
/// always sent here so cost-ceiling assertions work in fake mode too, per
/// design.md's "Determinism knobs"), then the literal <c>data: [DONE]</c>
/// terminator. Unlike Anthropic's SSE, OpenAI's frames carry no
/// <c>event:</c> line — every frame is a bare <c>data:</c> line. Reuses
/// <see cref="TextChunker"/> from the Anthropic response writer: the
/// chunking algorithm is a plain deterministic string splitter with no
/// Anthropic-specific dependency, so giving OpenAI its own copy would just
/// be the same splitter twice.
/// </summary>
public static class OpenAiSseWriter
{
    /// <summary>Writes every chunk for <paramref name="scripted"/> to <paramref name="response"/>, flushing after each one.</summary>
    public static async Task WriteStreamAsync(
        HttpResponse response,
        string chatCompletionId,
        string model,
        long createdUnixSeconds,
        string scenarioName,
        int requestIndex,
        FakeScriptResponse scripted,
        CancellationToken cancellationToken)
    {
        await OpenAiChunkWriter.WriteRoleChunkAsync(response, chatCompletionId, model, createdUnixSeconds, cancellationToken);

        var toolCallIndex = 0;
        foreach (var block in scripted.Content)
        {
            switch (block)
            {
                case FakeContentBlock.TextBlock text:
                    await OpenAiChunkWriter.WriteTextDeltasAsync(response, chatCompletionId, model, createdUnixSeconds, text.Text, cancellationToken);
                    break;
                case FakeContentBlock.ToolUseBlock toolUse:
                    await OpenAiChunkWriter.WriteToolCallDeltasAsync(response, chatCompletionId, model, createdUnixSeconds, scenarioName, requestIndex, toolCallIndex, toolUse, cancellationToken);
                    toolCallIndex++;
                    break;
                default:
                    throw new NotSupportedException($"unsupported fake content block type: {block.GetType()}");
            }
        }

        await OpenAiChunkWriter.WriteFinalChunkAsync(response, chatCompletionId, model, createdUnixSeconds, OpenAiFinishReasonMapper.Map(scripted.StopReason), cancellationToken);
        await OpenAiChunkWriter.WriteUsageChunkAsync(response, chatCompletionId, model, createdUnixSeconds, OpenAiResponseFactory.ResolveUsage(scripted.Usage), cancellationToken);
        await OpenAiChunkWriter.WriteDoneAsync(response, cancellationToken);
    }
}

/// <summary>The per-chunk write steps <see cref="OpenAiSseWriter"/> composes — extracted per class-layout-and-tooling.md §1a.</summary>
file static class OpenAiChunkWriter
{
    public static Task WriteRoleChunkAsync(HttpResponse response, string id, string model, long created, CancellationToken cancellationToken)
    {
        return WriteChunkAsync(response, id, model, created, [new ChunkChoice(0, new ChunkDelta("assistant", null, null), null)], usage: null, cancellationToken);
    }

    public static async Task WriteTextDeltasAsync(HttpResponse response, string id, string model, long created, string text, CancellationToken cancellationToken)
    {
        foreach (var chunk in TextChunker.Chunk(text, TextChunker.DefaultChunkSize))
        {
            await WriteChunkAsync(response, id, model, created, [new ChunkChoice(0, new ChunkDelta(null, chunk, null), null)], usage: null, cancellationToken);
        }
    }

    public static async Task WriteToolCallDeltasAsync(
        HttpResponse response,
        string id,
        string model,
        long created,
        string scenarioName,
        int requestIndex,
        int toolCallIndex,
        FakeContentBlock.ToolUseBlock toolUse,
        CancellationToken cancellationToken)
    {
        var openingDelta = new ToolCallDelta(
            toolCallIndex,
            DeterministicIds.ToolCallId(scenarioName, requestIndex, toolCallIndex),
            "function",
            new ToolCallFunctionDelta(toolUse.Name, string.Empty));
        await WriteChunkAsync(response, id, model, created, [new ChunkChoice(0, new ChunkDelta(null, null, [openingDelta]), null)], usage: null, cancellationToken);

        foreach (var chunk in TextChunker.Chunk(JsonSerializer.Serialize(toolUse.Input, JsonSerializerOptions.Web), TextChunker.DefaultChunkSize))
        {
            var continuationDelta = new ToolCallDelta(toolCallIndex, null, null, new ToolCallFunctionDelta(null, chunk));
            await WriteChunkAsync(response, id, model, created, [new ChunkChoice(0, new ChunkDelta(null, null, [continuationDelta]), null)], usage: null, cancellationToken);
        }
    }

    public static Task WriteFinalChunkAsync(HttpResponse response, string id, string model, long created, string finishReason, CancellationToken cancellationToken)
    {
        return WriteChunkAsync(response, id, model, created, [new ChunkChoice(0, new ChunkDelta(null, null, null), finishReason)], usage: null, cancellationToken);
    }

    public static Task WriteUsageChunkAsync(HttpResponse response, string id, string model, long created, OpenAiUsage usage, CancellationToken cancellationToken)
    {
        return WriteChunkAsync(response, id, model, created, [], usage, cancellationToken);
    }

    public static async Task WriteDoneAsync(HttpResponse response, CancellationToken cancellationToken)
    {
        await response.WriteAsync("data: [DONE]\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    public static async Task WriteChunkAsync(
        HttpResponse response,
        string id,
        string model,
        long created,
        IReadOnlyList<ChunkChoice> choices,
        OpenAiUsage? usage,
        CancellationToken cancellationToken)
    {
        var chunk = new ChatCompletionChunk(id, "chat.completion.chunk", created, model, choices, usage);
        await response.WriteAsync($"data: {JsonSerializer.Serialize(chunk, JsonSerializerOptions.Web)}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }
}

/// <summary>One <c>chat.completion.chunk</c> SSE frame's JSON payload. <see cref="Usage"/> is omitted on every chunk except the trailing usage-only one.</summary>
file sealed record ChatCompletionChunk(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("object")] string Object,
    [property: JsonPropertyName("created")] long Created,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("choices")] IReadOnlyList<ChunkChoice> Choices,
    [property: JsonPropertyName("usage"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] OpenAiUsage? Usage);

/// <summary>One entry of a chunk's <c>choices</c> array. <see cref="FinishReason"/> is legitimately <c>null</c> on every chunk but the final one — the real API sends the literal, not an omitted key.</summary>
file sealed record ChunkChoice(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("delta")] ChunkDelta Delta,
    [property: JsonPropertyName("finish_reason")] string? FinishReason);

/// <summary>
/// A chunk's <c>delta</c> object — unlike the non-streaming message, the
/// real API omits each field the chunk isn't updating (a content delta has
/// no <c>role</c> key at all, not a <c>null</c> one), so every field here
/// is omit-on-null.
/// </summary>
file sealed record ChunkDelta(
    [property: JsonPropertyName("role"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Role,
    [property: JsonPropertyName("content"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Content,
    [property: JsonPropertyName("tool_calls"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<ToolCallDelta>? ToolCalls);

/// <summary>One entry of a <c>delta.tool_calls</c> array. <see cref="Id"/>/<see cref="Type"/> appear only on the opening delta for that tool call.</summary>
file sealed record ToolCallDelta(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Id,
    [property: JsonPropertyName("type"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Type,
    [property: JsonPropertyName("function")] ToolCallFunctionDelta Function);

/// <summary><see cref="Name"/> appears only on the opening delta; every delta (opening and continuation) carries an <see cref="Arguments"/> chunk.</summary>
file sealed record ToolCallFunctionDelta(
    [property: JsonPropertyName("name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name,
    [property: JsonPropertyName("arguments")] string Arguments);
