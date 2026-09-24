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
/// <c>POST /v1/chat/completions</c> non-streaming responses: the OpenAI
/// wire shape (<c>choices[0].message</c>, <c>finish_reason</c>,
/// <c>usage.prompt_tokens</c>/<c>completion_tokens</c>/<c>total_tokens</c>),
/// tool_calls rendering, and that the same fakeScript engine (and the
/// same deterministic-id/usage-default machinery) already proven for
/// Anthropic in <c>FakeModelServerNonStreamingShould</c> serves this
/// shape too.
/// </summary>
public sealed class FakeModelServerOpenAiNonStreamingShould : IAsyncLifetime
{
    // boundary: assigned in InitializeAsync before any [Fact] can observe it.
    private FakeModelServer server = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var script = new FakeScriptBuilder()
            .WithScenarioName("openai-non-streaming")
            .RespondWithText("hello from the OpenAI-shape fake", new FakeUsage(InputTokens: 14, OutputTokens: 6))
            .RespondWithToolUse("get_weather", new Dictionary<string, object?> { ["city"] = "Prague", ["unit"] = "celsius" })
            .Build();

        server = new FakeModelServer(new FakeModelServerOptions { Script = script });
        await server.StartAsync();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await server.DisposeAsync();
    }

    [Fact(DisplayName = "Given a scripted text entry, when POST /v1/chat/completions non-streaming, then choices[0].message carries the deterministic id, text content and mapped usage")]
    public async Task ReturnScriptedTextResponseAsync()
    {
        using var client = new HttpClient { BaseAddress = server.BaseAddress };

        using var response = await PostAsync(client, UserMessageBody("hi"), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        root.GetProperty("id").GetString().ShouldBe("chatcmpl_openai-non-streaming_1");
        root.GetProperty("object").GetString().ShouldBe("chat.completion");
        root.GetProperty("usage").GetProperty("prompt_tokens").GetInt32().ShouldBe(14);
        root.GetProperty("usage").GetProperty("completion_tokens").GetInt32().ShouldBe(6);
        root.GetProperty("usage").GetProperty("total_tokens").GetInt32().ShouldBe(20);

        var choice = root.GetProperty("choices")[0];
        choice.GetProperty("finish_reason").GetString().ShouldBe("stop");
        choice.GetProperty("message").GetProperty("role").GetString().ShouldBe("assistant");
        choice.GetProperty("message").GetProperty("content").GetString().ShouldBe("hello from the OpenAI-shape fake");
        choice.GetProperty("message").GetProperty("tool_calls").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact(DisplayName = "Given a scripted tool_use entry, when POST /v1/chat/completions non-streaming, then message.tool_calls carries a function call with a deterministic id and JSON-string arguments, and finish_reason is tool_calls")]
    public async Task ReturnScriptedToolCallResponseAsync()
    {
        using var client = new HttpClient { BaseAddress = server.BaseAddress };
        await PostAsync(client, UserMessageBody("first turn"), TestContext.Current.CancellationToken);

        using var response = await PostAsync(client, UserMessageBody("second turn"), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(body);
        var choice = document.RootElement.GetProperty("choices")[0];

        choice.GetProperty("finish_reason").GetString().ShouldBe("tool_calls");
        var message = choice.GetProperty("message");
        message.GetProperty("content").ValueKind.ShouldBe(JsonValueKind.Null);

        var toolCall = message.GetProperty("tool_calls")[0];
        toolCall.GetProperty("id").GetString().ShouldBe("call_openai-non-streaming_2_0");
        toolCall.GetProperty("type").GetString().ShouldBe("function");
        toolCall.GetProperty("function").GetProperty("name").GetString().ShouldBe("get_weather");

        using var arguments = JsonDocument.Parse(toolCall.GetProperty("function").GetProperty("arguments").GetString()!);
        arguments.RootElement.GetProperty("city").GetString().ShouldBe("Prague");
        arguments.RootElement.GetProperty("unit").GetString().ShouldBe("celsius");
    }

    [Fact(DisplayName = "Given more requests than scripted entries, when the script runs out, then 500 with an OpenAI-shaped api_error naming the exhausted script")]
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
        // boundary: scripted server-error message, guaranteed non-null by OpenAiErrors.
        error.GetProperty("message").GetString()!.ShouldContain("exhausted");
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string json, CancellationToken cancellationToken)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await client.PostAsync("/v1/chat/completions", content, cancellationToken);
    }

    private static string UserMessageBody(string text, bool stream = false)
    {
        return /*lang=json,strict*/ $$"""
            {"model":"gpt-4o-mini","stream":{{(stream ? "true" : "false")}},"messages":[{"role":"user","content":"{{text}}"}]}
            """;
    }
}
