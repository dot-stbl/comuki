using System.Net;
using System.Text;
using System.Text.Json;
using Comuki.TestFakeModel.Scripting;
using Shouldly;
using Xunit;

namespace Comuki.TestFakeModel.Unit;

/// <summary>
/// The fakeScript matching engine: pure sequential consumption, indexed
/// lookup (declaration order doesn't matter), content-predicate
/// selection (the realistic "no tool_result yet" vs. "tool_result
/// attached" multi-turn case), and the fail-loud predicate-mismatch path.
/// </summary>
public sealed class FakeScriptMatchingShould
{
    [Fact(DisplayName = "Given three unpinned entries, when three requests arrive, then they're served in list order regardless of content")]
    public async Task ServeSequentialEntriesInListOrderAsync()
    {
        var script = new FakeScriptBuilder()
            .WithScenarioName("sequential")
            .RespondWithText("first")
            .RespondWithText("second")
            .RespondWithText("third")
            .Build();

        await using var server = new FakeModelServer(new FakeModelServerOptions { Script = script });
        await server.StartAsync(TestContext.Current.CancellationToken);
        using var client = new HttpClient { BaseAddress = server.BaseAddress };

        var texts = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            using var response = await PostAsync(client, "whatever", TestContext.Current.CancellationToken);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            using var document = JsonDocument.Parse(body);
            texts.Add(document.RootElement.GetProperty("content")[0].GetProperty("text").GetString()!);
        }

        texts.ShouldBe(["first", "second", "third"]);
    }

    [Fact(DisplayName = "Given entries declared out of order with explicit requestIndex, when requests arrive, then each is resolved by its index, not by declaration position")]
    public async Task ServeIndexedEntriesByRequestIndexAsync()
    {
        var script = new FakeScript("indexed",
        [
            new FakeScriptEntry(RequestIndex: 2, Match: null, Response: new FakeScriptResponse("end_turn", [new FakeContentBlock.TextBlock("entry for request #2")], null)),
            new FakeScriptEntry(RequestIndex: 1, Match: null, Response: new FakeScriptResponse("end_turn", [new FakeContentBlock.TextBlock("entry for request #1")], null)),
        ]);

        await using var server = new FakeModelServer(new FakeModelServerOptions { Script = script });
        await server.StartAsync(TestContext.Current.CancellationToken);
        using var client = new HttpClient { BaseAddress = server.BaseAddress };

        using var firstResponse = await PostAsync(client, "first", TestContext.Current.CancellationToken);
        using var secondResponse = await PostAsync(client, "second", TestContext.Current.CancellationToken);

        (await TextOfAsync(firstResponse)).ShouldBe("entry for request #1");
        (await TextOfAsync(secondResponse)).ShouldBe("entry for request #2");
    }

    [Fact(DisplayName = "Given entries pinned to hasToolResult, when a tool_result reply follows the first turn, then the second entry (not the first) is served — the realistic multi-turn shape")]
    public async Task SelectEntryByToolResultPredicateAsync()
    {
        var script = new FakeScriptBuilder()
            .WithScenarioName("predicate")
            .RespondWithToolUse("Read", new Dictionary<string, object?> { ["file_path"] = "a.cs" }, match: new FakeScriptMatch(null, HasToolResult: false))
            .RespondWithText("done reading, here's the summary", match: new FakeScriptMatch(null, HasToolResult: true))
            .Build();

        await using var server = new FakeModelServer(new FakeModelServerOptions { Script = script });
        await server.StartAsync(TestContext.Current.CancellationToken);
        using var client = new HttpClient { BaseAddress = server.BaseAddress };

        using var firstTurn = await PostAsync(client, "please read a.cs", TestContext.Current.CancellationToken);
        firstTurn.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var secondTurn = await PostRawAsync(client, ToolResultBody(), TestContext.Current.CancellationToken);

        secondTurn.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TextOfAsync(secondTurn)).ShouldBe("done reading, here's the summary");
    }

    [Fact(DisplayName = "Given an entry pinned to a lastUserMessageContains predicate, when the request text doesn't contain it, then 500 with a message describing the mismatch")]
    public async Task FailLoudlyOnPredicateMismatchAsync()
    {
        var script = new FakeScriptBuilder()
            .WithScenarioName("mismatch")
            .RespondWithText("only for OrderTotal", match: new FakeScriptMatch("OrderTotal", null))
            .Build();

        await using var server = new FakeModelServer(new FakeModelServerOptions { Script = script });
        await server.StartAsync(TestContext.Current.CancellationToken);
        using var client = new HttpClient { BaseAddress = server.BaseAddress };

        using var response = await PostAsync(client, "this ticket is about something unrelated", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("error").GetProperty("message").GetString()!.ShouldContain("did not satisfy");
    }

    private static async Task<string> TextOfAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("content")[0].GetProperty("text").GetString()!;
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string userText, CancellationToken cancellationToken)
    {
        var json = /*lang=json,strict*/ $$"""
            {"model":"claude-sonnet-5","max_tokens":1024,"stream":false,"messages":[{"role":"user","content":"{{userText}}"}]}
            """;
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await client.PostAsync("/v1/messages", content, cancellationToken);
    }

    private static async Task<HttpResponseMessage> PostRawAsync(HttpClient client, string rawJsonBody, CancellationToken cancellationToken)
    {
        using var content = new StringContent(rawJsonBody, Encoding.UTF8, "application/json");
        return await client.PostAsync("/v1/messages", content, cancellationToken);
    }

    private static string ToolResultBody()
    {
        return /*lang=json,strict*/ """
            {"model":"claude-sonnet-5","max_tokens":1024,"stream":false,"messages":[
              {"role":"user","content":"please read a.cs"},
              {"role":"assistant","content":[{"type":"tool_use","id":"toolu_predicate_1_0","name":"Read","input":{"file_path":"a.cs"}}]},
              {"role":"user","content":[{"type":"tool_result","tool_use_id":"toolu_predicate_1_0","content":"public class A {}"}]}
            ]}
            """;
    }
}
