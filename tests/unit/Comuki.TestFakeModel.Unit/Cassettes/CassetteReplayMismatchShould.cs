using System.Net;
using System.Text;
using System.Text.Json;
using Comuki.TestFakeModel.Anthropic.Response;
using Comuki.TestFakeModel.Cassettes;
using Comuki.TestFakeModel.Cassettes.Hosting;
using Comuki.TestFakeModel.Cassettes.Matching;
using Comuki.TestFakeModel.Cassettes.Wire;
using Comuki.TestFakeModel.Scripting.Model.Response;
using Shouldly;
using Xunit;

namespace Comuki.TestFakeModel.Unit.Cassettes;

/// <summary>
/// Replay mode's fail-loud contract (design.md task 5.4 / tasks.md
/// acceptance): a request-shape mismatch against the next expected
/// cassette exchange — or the cassette running out entirely — is a typed
/// error with a readable expected-vs-observed diff, never a silent
/// passthrough of the wrong turn.
/// </summary>
public sealed class CassetteReplayMismatchShould
{
    [Fact(DisplayName = "Given a cassette with two exchanges, when the first request matches but the second doesn't, then the second request fails with a typed mismatch error naming both the expected and observed match key")]
    public async Task FailLoudlyOnRequestShapeMismatchAsync()
    {
        var cassettePath = WriteFixtureCassette(exchangeCount: 2);
        try
        {
            await using var replay = new CassetteModelServer(new CassetteModelServerOptions { Mode = CassetteModelMode.Replay, CassettePath = cassettePath });
            await replay.StartAsync(TestContext.Current.CancellationToken);
            using var client = new HttpClient { BaseAddress = replay.BaseAddress };

            using var firstResponse = await PostAsync(client, "hi", TestContext.Current.CancellationToken);
            firstResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

            using var secondResponse = await PostAsync(client, "a completely different message than the cassette expects", TestContext.Current.CancellationToken);

            secondResponse.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
            var body = await secondResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            using var document = JsonDocument.Parse(body);
            // boundary: scripted server-error message, guaranteed non-null by AnthropicErrors.
            var message = document.RootElement.GetProperty("error").GetProperty("message").GetString()!;
            message.ShouldContain("replay mismatch");
            message.ShouldContain("expected:");
            message.ShouldContain("observed:");
        }
        finally
        {
            File.Delete(cassettePath);
        }
    }

    [Fact(DisplayName = "Given a cassette with one exchange, when a second request arrives, then it fails with a typed exhaustion error naming the exhausted cassette")]
    public async Task FailLoudlyWhenCassetteExhaustedAsync()
    {
        var cassettePath = WriteFixtureCassette(exchangeCount: 1);
        try
        {
            await using var replay = new CassetteModelServer(new CassetteModelServerOptions { Mode = CassetteModelMode.Replay, CassettePath = cassettePath });
            await replay.StartAsync(TestContext.Current.CancellationToken);
            using var client = new HttpClient { BaseAddress = replay.BaseAddress };

            using var firstResponse = await PostAsync(client, "hi", TestContext.Current.CancellationToken);
            firstResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

            using var secondResponse = await PostAsync(client, "anything at all", TestContext.Current.CancellationToken);

            secondResponse.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
            var body = await secondResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            using var document = JsonDocument.Parse(body);
            document.RootElement.GetProperty("error").GetProperty("message").GetString()!.ShouldContain("exhausted");
        }
        finally
        {
            File.Delete(cassettePath);
        }
    }

    private static string WriteFixtureCassette(int exchangeCount)
    {
        var exchanges = new List<CassetteExchange>
        {
            new(
                new CassetteRequest("POST", "/v1/messages", new CassetteRequestMatch(CassetteMatchKeyBuilder.HashLastUserMessage("hi"), ToolResultPresent: false)),
                new CassetteResponse(200, Streamed: false, BuildBody("msg_fixture_1", "first scripted reply"), Events: null)),
        };

        if (exchangeCount > 1)
        {
            exchanges.Add(new CassetteExchange(
                new CassetteRequest("POST", "/v1/messages", new CassetteRequestMatch(CassetteMatchKeyBuilder.HashLastUserMessage("expected second message"), ToolResultPresent: false)),
                new CassetteResponse(200, Streamed: false, BuildBody("msg_fixture_2", "second scripted reply"), Events: null)));
        }

        var cassette = new CassetteFile(CassetteFile.CurrentSchemaVersion, "fixture", DateTimeOffset.UnixEpoch, "fixture-model", exchanges);
        var path = Path.Combine(Path.GetTempPath(), $"comuki-testfakemodel-mismatch-{Guid.NewGuid():N}.cassette.json");
        File.WriteAllText(path, JsonSerializer.Serialize(cassette, JsonSerializerOptions.Web));
        return path;
    }

    private static JsonElement BuildBody(string messageId, string text)
    {
        var response = AnthropicResponseFactory.BuildNonStreaming(
            messageId,
            "claude-sonnet-5",
            "fixture",
            1,
            new FakeScriptResponse("end_turn", [new FakeContentBlock.TextBlock(text)], new FakeUsage(5, 2)));
        return JsonSerializer.SerializeToElement(response, JsonSerializerOptions.Web);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string text, CancellationToken cancellationToken)
    {
        var json = /*lang=json,strict*/ $$"""
            {"model":"claude-sonnet-5","max_tokens":1024,"stream":false,"messages":[{"role":"user","content":"{{text}}"}]}
            """;
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await client.PostAsync("/v1/messages", content, cancellationToken);
    }
}
