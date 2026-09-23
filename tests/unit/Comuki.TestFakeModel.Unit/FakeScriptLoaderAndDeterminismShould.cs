using System.Text;
using System.Text.Json;
using Comuki.TestFakeModel.Hosting;
using Comuki.TestFakeModel.Scripting.Building;
using Comuki.TestFakeModel.Scripting.Loading;
using Comuki.TestFakeModel.Scripting.Model;
using Comuki.TestFakeModel.Scripting.Model.Response;
using Shouldly;
using Xunit;

namespace Comuki.TestFakeModel.Unit;

/// <summary>
/// <see cref="FakeScriptLoader"/> (JSON and YAML on disk) and the
/// determinism guarantee: two independently-started servers running the
/// same script under the same scenario name produce byte-identical ids
/// and usage, because nothing in the pipeline reads the wall clock or a
/// random source.
/// </summary>
public sealed class FakeScriptLoaderAndDeterminismShould
{
    [Fact(DisplayName = "Given a fakeScript JSON file, when loaded, then its scenario name and entries serve exactly what the file scripted")]
    public async Task LoadFromJsonFileAsync()
    {
        var script = FakeScriptLoader.LoadFromFile(FixturePath("loader-demo.fake.json"));

        script.ScenarioName.ShouldBe("loader-demo");
        script.Entries.Count.ShouldBe(1);

        await using var server = new FakeModelServer(new FakeModelServerOptions { Script = script });
        await server.StartAsync(TestContext.Current.CancellationToken);
        using var client = new HttpClient { BaseAddress = server.BaseAddress };

        using var response = await PostAsync(client, "hi", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(body);

        document.RootElement.GetProperty("id").GetString().ShouldBe("msg_loader-demo_1");
        document.RootElement.GetProperty("content")[0].GetProperty("text").GetString().ShouldBe("loaded from json");
        document.RootElement.GetProperty("usage").GetProperty("input_tokens").GetInt32().ShouldBe(5);
    }

    [Fact(DisplayName = "Given a fakeScript YAML file with a text entry and a tool_use entry, when loaded, then both entries — including the nested tool input — map onto the domain model correctly")]
    public async Task LoadFromYamlFileAsync()
    {
        var script = FakeScriptLoader.LoadFromFile(FixturePath("loader-demo.fake.yaml"));

        script.ScenarioName.ShouldBe("loader-demo-yaml");
        script.Entries.Count.ShouldBe(2);

        await using var server = new FakeModelServer(new FakeModelServerOptions { Script = script });
        await server.StartAsync(TestContext.Current.CancellationToken);
        using var client = new HttpClient { BaseAddress = server.BaseAddress };

        using var firstResponse = await PostAsync(client, "hi", TestContext.Current.CancellationToken);
        var firstBody = await firstResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var firstDocument = JsonDocument.Parse(firstBody);
        firstDocument.RootElement.GetProperty("content")[0].GetProperty("text").GetString().ShouldBe("loaded from yaml");

        using var secondResponse = await PostAsync(client, "now edit it", TestContext.Current.CancellationToken);
        var secondBody = await secondResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var secondDocument = JsonDocument.Parse(secondBody);
        var block = secondDocument.RootElement.GetProperty("content")[0];
        block.GetProperty("type").GetString().ShouldBe("tool_use");
        block.GetProperty("name").GetString().ShouldBe("Edit");
        block.GetProperty("input").GetProperty("file_path").GetString().ShouldBe("src/Example.cs");
        var replacement = block.GetProperty("input").GetProperty("replacements")[0];
        replacement.GetProperty("from").GetString().ShouldBe("foo");
        replacement.GetProperty("to").GetString().ShouldBe("bar");
    }

    [Fact(DisplayName = "Given the same scenario name and script on two independently-started servers, when the same request sequence runs on each, then ids and usage are byte-identical")]
    public async Task ProduceIdenticalIdsAcrossIndependentServerInstancesAsync()
    {
        static FakeScript BuildScript()
        {
            return new FakeScriptBuilder()
                .WithScenarioName("determinism")
                .RespondWithText("first", new FakeUsage(InputTokens: 9, OutputTokens: 2))
                .RespondWithToolUse("Read", new Dictionary<string, object?> { ["file_path"] = "x.cs" })
                .Build();
        }

        await using var serverA = new FakeModelServer(new FakeModelServerOptions { Script = BuildScript() });
        await using var serverB = new FakeModelServer(new FakeModelServerOptions { Script = BuildScript() });
        await serverA.StartAsync(TestContext.Current.CancellationToken);
        await serverB.StartAsync(TestContext.Current.CancellationToken);

        using var clientA = new HttpClient { BaseAddress = serverA.BaseAddress };
        using var clientB = new HttpClient { BaseAddress = serverB.BaseAddress };

        using var firstA = await PostAsync(clientA, "hi", TestContext.Current.CancellationToken);
        using var firstB = await PostAsync(clientB, "hi", TestContext.Current.CancellationToken);
        using var secondA = await PostAsync(clientA, "again", TestContext.Current.CancellationToken);
        using var secondB = await PostAsync(clientB, "again", TestContext.Current.CancellationToken);

        (await BodyAsync(firstA)).ShouldBe(await BodyAsync(firstB));
        (await BodyAsync(secondA)).ShouldBe(await BodyAsync(secondB));
    }

    private static string FixturePath(string fileName)
    {
        return Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
    }

    private static async Task<string> BodyAsync(HttpResponseMessage response)
    {
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string userText, CancellationToken cancellationToken)
    {
        var json = /*lang=json,strict*/ $$"""
            {"model":"claude-sonnet-5","max_tokens":1024,"stream":false,"messages":[{"role":"user","content":"{{userText}}"}]}
            """;
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await client.PostAsync("/v1/messages", content, cancellationToken);
    }
}
