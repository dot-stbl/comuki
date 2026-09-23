using System.Net;
using System.Text;
using System.Text.Json;
using Comuki.TestFakeModel.Hosting;
using Comuki.TestFakeModel.Scripting.Building;
using Comuki.TestFakeModel.Scripting.Model.Response;
using Shouldly;
using Xunit;

namespace Comuki.TestFakeModel.Unit.OpenAi;

/// <summary>
/// <c>POST /v1/chat/completions</c> with <c>stream: true</c>: OpenAI's
/// <c>chat.completion.chunk</c> SSE framing — no <c>event:</c> line (every
/// frame is a bare <c>data:</c> line, unlike Anthropic), a role-only
/// opening chunk, content/tool_calls deltas that reassemble the scripted
/// content, a closing chunk carrying <c>finish_reason</c>, a trailing
/// usage-only chunk, then the literal <c>data: [DONE]</c> terminator.
/// </summary>
public sealed class FakeModelServerOpenAiStreamingShould : IAsyncLifetime
{
    private const string ScriptedText =
        "This is a longer scripted reply so the deterministic chunker has to split it into more than one content delta.";

    private const string DoneMarker = "[DONE]";

    // boundary: assigned in InitializeAsync before any [Fact] can observe it.
    private FakeModelServer server = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var script = new FakeScriptBuilder()
            .WithScenarioName("openai-streaming")
            .RespondWithText(ScriptedText, new FakeUsage(InputTokens: 18, OutputTokens: 27))
            .RespondWithToolUse("run_command", new Dictionary<string, object?> { ["command"] = "dotnet test" })
            .Build();

        server = new FakeModelServer(new FakeModelServerOptions { Script = script });
        await server.StartAsync();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await server.DisposeAsync();
    }

    [Fact(DisplayName = "Given a scripted text entry, when stream:true, then every SSE frame is a bare data: line (no event: line), the deltas reassemble the scripted text, and the stream ends with a usage chunk then [DONE]")]
    public async Task StreamTextInCorrectFramingAsync()
    {
        using var client = new HttpClient { BaseAddress = server.BaseAddress };

        using var response = await PostStreamAsync(client, "hi", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        // boundary: a 200 SSE response always carries a Content-Type header.
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/event-stream");

        var rawBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        rawBody.ShouldNotContain("event: ");
        rawBody.ShouldEndWith($"data: {DoneMarker}\n\n");

        var chunks = ParseChunks(rawBody);
        chunks[0].GetProperty("choices")[0].GetProperty("delta").GetProperty("role").GetString().ShouldBe("assistant");
        chunks[0].GetProperty("id").GetString().ShouldBe("chatcmpl_openai-streaming_1");

        var contentChunks = chunks.Skip(1).ToList();
        var usageChunk = contentChunks.Single(static chunk => chunk.GetProperty("choices").GetArrayLength() == 0);
        var finishChunk = contentChunks.Last(static chunk => chunk.GetProperty("choices").GetArrayLength() > 0);

        var reassembled = new StringBuilder();
        foreach (var chunk in contentChunks)
        {
            var choices = chunk.GetProperty("choices");
            if (choices.GetArrayLength() > 0 && choices[0].GetProperty("delta").TryGetProperty("content", out var contentElement))
            {
                reassembled.Append(contentElement.GetString());
            }
        }

        reassembled.ToString().ShouldBe(ScriptedText);
        finishChunk.GetProperty("choices")[0].GetProperty("finish_reason").GetString().ShouldBe("stop");
        usageChunk.GetProperty("usage").GetProperty("prompt_tokens").GetInt32().ShouldBe(18);
        usageChunk.GetProperty("usage").GetProperty("completion_tokens").GetInt32().ShouldBe(27);
    }

    [Fact(DisplayName = "Given a scripted tool_use entry, when stream:true, then the opening tool_calls delta carries id/type/name, continuation deltas omit them and reassemble the arguments JSON, and the stream finishes with finish_reason=tool_calls")]
    public async Task StreamToolCallInCorrectFramingAsync()
    {
        using var client = new HttpClient { BaseAddress = server.BaseAddress };
        await PostStreamAsync(client, "first turn", TestContext.Current.CancellationToken);

        using var response = await PostStreamAsync(client, "second turn", TestContext.Current.CancellationToken);
        var rawBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var chunks = ParseChunks(rawBody);

        var toolCallDeltas = chunks
            .Where(static chunk => chunk.GetProperty("choices").GetArrayLength() > 0 && chunk.GetProperty("choices")[0].GetProperty("delta").TryGetProperty("tool_calls", out _))
            .Select(static chunk => chunk.GetProperty("choices")[0].GetProperty("delta").GetProperty("tool_calls")[0])
            .ToList();

        toolCallDeltas.ShouldNotBeEmpty();
        var opening = toolCallDeltas[0];
        opening.GetProperty("id").GetString().ShouldBe("call_openai-streaming_2_0");
        opening.GetProperty("type").GetString().ShouldBe("function");
        opening.GetProperty("function").GetProperty("name").GetString().ShouldBe("run_command");
        opening.GetProperty("function").GetProperty("arguments").GetString().ShouldBe(string.Empty);

        var argumentsJson = new StringBuilder();
        foreach (var continuation in toolCallDeltas.Skip(1))
        {
            continuation.TryGetProperty("id", out _).ShouldBeFalse();
            continuation.TryGetProperty("type", out _).ShouldBeFalse();
            argumentsJson.Append(continuation.GetProperty("function").GetProperty("arguments").GetString());
        }

        using var reassembledArguments = JsonDocument.Parse(argumentsJson.ToString());
        reassembledArguments.RootElement.GetProperty("command").GetString().ShouldBe("dotnet test");

        var finishReasons = chunks
            .Where(static chunk => chunk.GetProperty("choices").GetArrayLength() > 0)
            .Select(static chunk => chunk.GetProperty("choices")[0].GetProperty("finish_reason"))
            .Where(static finishReason => finishReason.ValueKind == JsonValueKind.String)
            .ToList();
        finishReasons.ShouldHaveSingleItem();
        finishReasons[0].GetString().ShouldBe("tool_calls");
    }

    /// <summary>Parses every <c>data:</c> line (including the terminal <c>[DONE]</c>, represented as an empty object) into cloned, independently-owned <see cref="JsonElement"/>s.</summary>
    private static List<JsonElement> ParseChunks(string rawBody)
    {
        return [.. rawBody
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(static line => line.StartsWith("data: ", StringComparison.Ordinal))
            .Select(static line => line["data: ".Length..])
            .Where(static data => data != DoneMarker)
            .Select(static data =>
            {
                using var document = JsonDocument.Parse(data);
                return document.RootElement.Clone();
            })];
    }

    private static async Task<HttpResponseMessage> PostStreamAsync(HttpClient client, string userText, CancellationToken cancellationToken)
    {
        var json = /*lang=json,strict*/ $$"""
            {"model":"gpt-4o-mini","stream":true,"messages":[{"role":"user","content":"{{userText}}"}]}
            """;
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await client.PostAsync("/v1/chat/completions", content, cancellationToken);
    }
}
