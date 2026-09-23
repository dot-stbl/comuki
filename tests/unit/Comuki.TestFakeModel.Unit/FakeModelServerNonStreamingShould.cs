using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Comuki.TestFakeModel.Scripting;
using Shouldly;
using Xunit;

namespace Comuki.TestFakeModel.Unit;

/// <summary>
/// <c>POST /v1/messages</c> non-streaming responses: content-block shape,
/// deterministic ids, credential pass-through, and the two failure paths
/// (malformed body, script exhausted).
/// </summary>
public sealed class FakeModelServerNonStreamingShould : IAsyncLifetime
{
    private FakeModelServer server = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var script = new FakeScriptBuilder()
            .WithScenarioName("non-streaming")
            .RespondWithText("hello from the fake model", new FakeUsage(InputTokens: 11, OutputTokens: 4))
            .RespondWithToolUse("Edit", new Dictionary<string, object?> { ["file_path"] = "src/OrderTotal.cs", ["line"] = 42 })
            .Build();

        server = new FakeModelServer(new FakeModelServerOptions { Script = script });
        await server.StartAsync();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await server.DisposeAsync();
    }

    [Fact(DisplayName = "Given a scripted text entry, when POST /v1/messages non-streaming, then the body carries the deterministic id, text block and scripted usage")]
    public async Task ReturnScriptedTextResponseAsync()
    {
        using var client = new HttpClient { BaseAddress = server.BaseAddress };

        using var response = await PostAsync(client, UserMessageBody("hi"), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        root.GetProperty("id").GetString().ShouldBe("msg_non-streaming_1");
        root.GetProperty("stop_reason").GetString().ShouldBe("end_turn");
        root.GetProperty("usage").GetProperty("input_tokens").GetInt32().ShouldBe(11);
        root.GetProperty("usage").GetProperty("output_tokens").GetInt32().ShouldBe(4);

        var content = root.GetProperty("content");
        content.GetArrayLength().ShouldBe(1);
        content[0].GetProperty("type").GetString().ShouldBe("text");
        content[0].GetProperty("text").GetString().ShouldBe("hello from the fake model");
    }

    [Fact(DisplayName = "Given a scripted tool_use entry, when POST /v1/messages non-streaming, then the body carries a tool_use block with a deterministic id and the scripted input")]
    public async Task ReturnScriptedToolUseResponseAsync()
    {
        using var client = new HttpClient { BaseAddress = server.BaseAddress };
        await PostAsync(client, UserMessageBody("first turn"), TestContext.Current.CancellationToken);

        using var response = await PostAsync(client, UserMessageBody("second turn"), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        root.GetProperty("id").GetString().ShouldBe("msg_non-streaming_2");
        root.GetProperty("stop_reason").GetString().ShouldBe("tool_use");

        var block = root.GetProperty("content")[0];
        block.GetProperty("type").GetString().ShouldBe("tool_use");
        block.GetProperty("id").GetString().ShouldBe("toolu_non-streaming_2_0");
        block.GetProperty("name").GetString().ShouldBe("Edit");
        block.GetProperty("input").GetProperty("file_path").GetString().ShouldBe("src/OrderTotal.cs");
        block.GetProperty("input").GetProperty("line").GetInt32().ShouldBe(42);
    }

    [Fact(DisplayName = "Given any (or no) credential header, when POST /v1/messages, then the request succeeds and the header is recorded, not enforced")]
    public async Task AcceptAndRecordAnyCredentialAsync()
    {
        using var client = new HttpClient { BaseAddress = server.BaseAddress };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/messages")
        {
            Content = new StringContent(UserMessageBody("hi"), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "unverified-virtual-key");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        server.Requests.ShouldHaveSingleItem();
        server.Requests[0].AuthorizationHeader.ShouldBe("Bearer unverified-virtual-key");
        server.Requests[0].ApiKeyHeader.ShouldBeNull();
        server.Requests[0].Stream.ShouldBeFalse();
        server.Requests[0].LastUserMessageText.ShouldBe("hi");
    }

    [Fact(DisplayName = "Given a malformed JSON body, when POST /v1/messages, then 400 with an Anthropic-shaped invalid_request_error")]
    public async Task RejectMalformedBodyAsync()
    {
        using var client = new HttpClient { BaseAddress = server.BaseAddress };
        using var content = new StringContent("{not json", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/v1/messages", content, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("error").GetProperty("type").GetString().ShouldBe("invalid_request_error");
    }

    [Fact(DisplayName = "Given more requests than scripted entries, when the script runs out, then 500 with an api_error naming the exhausted script")]
    public async Task FailLoudlyWhenScriptExhaustedAsync()
    {
        using var client = new HttpClient { BaseAddress = server.BaseAddress };
        await PostAsync(client, UserMessageBody("turn 1"), TestContext.Current.CancellationToken);
        await PostAsync(client, UserMessageBody("turn 2"), TestContext.Current.CancellationToken);

        using var response = await PostAsync(client, UserMessageBody("turn 3 — no entry left"), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(body);
        var error = document.RootElement.GetProperty("error");
        error.GetProperty("type").GetString().ShouldBe("api_error");
        error.GetProperty("message").GetString()!.ShouldContain("exhausted");
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string json, CancellationToken cancellationToken)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await client.PostAsync("/v1/messages", content, cancellationToken);
    }

    private static string UserMessageBody(string text, bool stream = false)
    {
        return /*lang=json,strict*/ $$"""
            {"model":"claude-sonnet-5","max_tokens":1024,"stream":{{(stream ? "true" : "false")}},"messages":[{"role":"user","content":"{{text}}"}]}
            """;
    }
}
