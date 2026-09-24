using System.Net;
using System.Text;
using System.Text.Json;
using Comuki.TestFakeModel.Hosting;
using Comuki.TestFakeModel.Scripting.Building;
using Comuki.TestFakeModel.Scripting.Model.Response;
using Shouldly;
using Xunit;

namespace Comuki.TestFakeModel.Unit;

/// <summary>
/// <c>POST /v1/messages</c> with <c>stream: true</c>: SSE event framing
/// (event ordering, exact event types) and that streamed
/// <c>text_delta</c>/<c>input_json_delta</c> chunks reassemble into the
/// scripted content.
/// </summary>
public sealed class FakeModelServerStreamingShould : IAsyncLifetime
{
    private const string ScriptedText =
        "This is a longer scripted reply so the deterministic chunker has to split it into more than one text_delta event.";

    // boundary: assigned in InitializeAsync before any [Fact] can observe it.
    private FakeModelServer server = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var script = new FakeScriptBuilder()
            .WithScenarioName("streaming")
            .RespondWithText(ScriptedText, new FakeUsage(InputTokens: 20, OutputTokens: 30))
            .RespondWithToolUse("Bash", new Dictionary<string, object?> { ["command"] = "dotnet test" })
            .Build();

        server = new FakeModelServer(new FakeModelServerOptions { Script = script });
        await server.StartAsync();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await server.DisposeAsync();
    }

    [Fact(DisplayName = "Given a scripted text entry, when stream:true, then the SSE event sequence is message_start, content_block_start, N x content_block_delta, content_block_stop, message_delta, message_stop and the deltas reassemble the scripted text")]
    public async Task StreamTextInCorrectFramingAsync()
    {
        using var client = new HttpClient { BaseAddress = server.BaseAddress };

        using var response = await PostStreamAsync(client, "hi", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        // boundary: a 200 SSE response always carries a Content-Type header.
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/event-stream");

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var events = SseTestHelpers.Parse(body);

        events.Count.ShouldBeGreaterThan(4);
        events[0].EventType.ShouldBe("message_start");
        events[1].EventType.ShouldBe("content_block_start");
        events[^3].EventType.ShouldBe("content_block_stop");
        events[^2].EventType.ShouldBe("message_delta");
        events[^1].EventType.ShouldBe("message_stop");

        var deltaEvents = events.Skip(2).Take(events.Count - 5).ToList();
        deltaEvents.ShouldNotBeEmpty();
        deltaEvents.ShouldAllBe(static deltaEvent => deltaEvent.EventType == "content_block_delta");

        var reassembled = new StringBuilder();
        foreach (var delta in deltaEvents)
        {
            using var deltaDocument = JsonDocument.Parse(delta.Data);
            reassembled.Append(deltaDocument.RootElement.GetProperty("delta").GetProperty("text").GetString());
        }

        reassembled.ToString().ShouldBe(ScriptedText);

        using var messageStartDocument = JsonDocument.Parse(events[0].Data);
        messageStartDocument.RootElement.GetProperty("message").GetProperty("id").GetString().ShouldBe("msg_streaming_1");
        messageStartDocument.RootElement.GetProperty("message").GetProperty("usage").GetProperty("input_tokens").GetInt32().ShouldBe(20);

        using var messageDeltaDocument = JsonDocument.Parse(events[^2].Data);
        messageDeltaDocument.RootElement.GetProperty("delta").GetProperty("stop_reason").GetString().ShouldBe("end_turn");
        messageDeltaDocument.RootElement.GetProperty("usage").GetProperty("output_tokens").GetInt32().ShouldBe(30);
    }

    [Fact(DisplayName = "Given a scripted tool_use entry, when stream:true, then content_block_start opens an empty tool_use input and input_json_delta chunks reassemble the scripted arguments")]
    public async Task StreamToolUseInCorrectFramingAsync()
    {
        using var client = new HttpClient { BaseAddress = server.BaseAddress };
        await PostStreamAsync(client, "first turn", TestContext.Current.CancellationToken);

        using var response = await PostStreamAsync(client, "second turn", TestContext.Current.CancellationToken);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var events = SseTestHelpers.Parse(body);

        using var startDocument = JsonDocument.Parse(events[1].Data);
        var startBlock = startDocument.RootElement.GetProperty("content_block");
        startBlock.GetProperty("type").GetString().ShouldBe("tool_use");
        startBlock.GetProperty("id").GetString().ShouldBe("toolu_streaming_2_0");
        startBlock.GetProperty("name").GetString().ShouldBe("Bash");
        startBlock.GetProperty("input").EnumerateObject().ShouldBeEmpty();

        var deltaEvents = events.Skip(2).Take(events.Count - 5).ToList();
        deltaEvents.ShouldNotBeEmpty();

        var partialJson = new StringBuilder();
        foreach (var delta in deltaEvents)
        {
            using var deltaDocument = JsonDocument.Parse(delta.Data);
            delta.EventType.ShouldBe("content_block_delta");
            deltaDocument.RootElement.GetProperty("delta").GetProperty("type").GetString().ShouldBe("input_json_delta");
            partialJson.Append(deltaDocument.RootElement.GetProperty("delta").GetProperty("partial_json").GetString());
        }

        using var reassembledInput = JsonDocument.Parse(partialJson.ToString());
        reassembledInput.RootElement.GetProperty("command").GetString().ShouldBe("dotnet test");

        using var messageDeltaDocument = JsonDocument.Parse(events[^2].Data);
        messageDeltaDocument.RootElement.GetProperty("delta").GetProperty("stop_reason").GetString().ShouldBe("tool_use");
    }

    private static async Task<HttpResponseMessage> PostStreamAsync(HttpClient client, string userText, CancellationToken cancellationToken)
    {
        var json = /*lang=json,strict*/ $$"""
            {"model":"claude-sonnet-5","max_tokens":1024,"stream":true,"messages":[{"role":"user","content":"{{userText}}"}]}
            """;
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await client.PostAsync("/v1/messages", content, cancellationToken);
    }
}
