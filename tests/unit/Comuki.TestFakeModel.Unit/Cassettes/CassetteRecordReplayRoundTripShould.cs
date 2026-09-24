using System.Net;
using System.Text;
using Comuki.TestFakeModel.Cassettes.Hosting;
using Comuki.TestFakeModel.Hosting;
using Comuki.TestFakeModel.Scripting.Building;
using Shouldly;
using Xunit;

namespace Comuki.TestFakeModel.Unit.Cassettes;

/// <summary>
/// The design.md WS5 acceptance criterion: "a cassette recorded against
/// WS4's in-process server round-trips through record → replay with
/// byte-identical served responses". Uses a <see cref="FakeModelServer"/>
/// (fake mode) as the "real upstream" record mode forwards to — the task
/// brief's "record-mode test uses an in-process fake upstream", and it
/// happens to be the exact server this cassette format was designed
/// against. Exercises both wire shapes and both streaming modes in one
/// round trip: Anthropic non-streaming, OpenAI streaming tool_calls.
/// </summary>
public sealed class CassetteRecordReplayRoundTripShould
{
    [Fact(DisplayName = "Given a fake-mode server as the upstream, when record mode forwards an Anthropic non-streaming exchange and an OpenAI streaming tool_calls exchange, and replay mode later serves the written cassette, then every response byte matches what record mode captured")]
    public async Task RoundTripRecordedResponsesByteIdenticallyAsync()
    {
        var cassettePath = Path.Combine(Path.GetTempPath(), $"comuki-testfakemodel-roundtrip-{Guid.NewGuid():N}.cassette.json");
        try
        {
            var upstreamScript = new FakeScriptBuilder()
                .WithScenarioName("roundtrip")
                .RespondWithText("the null check is already in place")
                .RespondWithToolUse("run_tests", new Dictionary<string, object?> { ["suite"] = "unit" })
                .Build();

            await using var upstream = new FakeModelServer(new FakeModelServerOptions { Script = upstreamScript });
            await upstream.StartAsync(TestContext.Current.CancellationToken);

            string recordedNonStreamingBody;
            string recordedStreamingBody;
            await using (var recorder = new CassetteModelServer(new CassetteModelServerOptions
            {
                Mode = CassetteModelMode.Record,
                CassettePath = cassettePath,
                Scenario = "roundtrip",
                RecordedAgainst = "fake-upstream",
                UpstreamBaseUrl = upstream.BaseAddress,
            }))
            {
                await recorder.StartAsync(TestContext.Current.CancellationToken);
                using var client = new HttpClient { BaseAddress = recorder.BaseAddress };

                using var nonStreamingResponse = await PostAsync(client, "/v1/messages", AnthropicBody("please add a null check", stream: false), TestContext.Current.CancellationToken);
                nonStreamingResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
                recordedNonStreamingBody = await nonStreamingResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

                using var streamingResponse = await PostAsync(client, "/v1/chat/completions", OpenAiBody("run the tests", stream: true), TestContext.Current.CancellationToken);
                streamingResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
                recordedStreamingBody = await streamingResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            }

            File.Exists(cassettePath).ShouldBeTrue();

            await using var replay = new CassetteModelServer(new CassetteModelServerOptions
            {
                Mode = CassetteModelMode.Replay,
                CassettePath = cassettePath,
            });
            await replay.StartAsync(TestContext.Current.CancellationToken);
            using var replayClient = new HttpClient { BaseAddress = replay.BaseAddress };

            using var replayedNonStreamingResponse = await PostAsync(replayClient, "/v1/messages", AnthropicBody("please add a null check", stream: false), TestContext.Current.CancellationToken);
            replayedNonStreamingResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await replayedNonStreamingResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe(recordedNonStreamingBody);

            using var replayedStreamingResponse = await PostAsync(replayClient, "/v1/chat/completions", OpenAiBody("run the tests", stream: true), TestContext.Current.CancellationToken);
            replayedStreamingResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await replayedStreamingResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe(recordedStreamingBody);
        }
        finally
        {
            File.Delete(cassettePath);
        }
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, string json, CancellationToken cancellationToken)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await client.PostAsync(path, content, cancellationToken);
    }

    private static string AnthropicBody(string text, bool stream)
    {
        return /*lang=json,strict*/ $$"""
            {"model":"claude-sonnet-5","max_tokens":1024,"stream":{{(stream ? "true" : "false")}},"messages":[{"role":"user","content":"{{text}}"}]}
            """;
    }

    private static string OpenAiBody(string text, bool stream)
    {
        return /*lang=json,strict*/ $$"""
            {"model":"gpt-4o-mini","stream":{{(stream ? "true" : "false")}},"messages":[{"role":"user","content":"{{text}}"}]}
            """;
    }
}
