using System.Text.Json;
using Comuki.Host.Translator.Parsing;
using Shouldly;
using Xunit;

namespace Comuki.Host.Translator.Unit.StreamJson;

/// <summary>
/// Unit tests for <see cref="StreamJsonParser"/> against recorded pi stream-json
/// fixtures. Locks the contract: malformed input does not throw, unknown event
/// types surface as <see cref="PiEvent.UnknownEvent"/>, the four modelled
/// event types (system / user / assistant / result) are recognised.
/// </summary>
public sealed class StreamJsonParserShould
{
    private static async Task<string> ReadFixtureAsync(string fileName)
    {
        return await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RecognizeSystemEventAsync()
    {
        var events = StreamJsonParser.ParseLine(await ReadFixtureAsync("event-system.json")).ToList();

        var system = events.ShouldHaveSingleItem().ShouldBeOfType<PiEvent.SystemEvent>();
        system.Subtype.ShouldBe("init");
        system.Cwd.ShouldBe("/work");
        system.Tools.ShouldBe(["Read", "Write", "Edit", "Bash"]);
    }

    [Fact]
    public async Task RecognizeUserEventWithStringContentAsync()
    {
        var events = StreamJsonParser.ParseLine(await ReadFixtureAsync("event-user.json")).ToList();

        var user = events.ShouldHaveSingleItem().ShouldBeOfType<PiEvent.UserEvent>();
        user.Content.ShouldBe("Say hello in exactly one word");
    }

    [Fact]
    public async Task RecognizeAssistantTextEventAsync()
    {
        var events = StreamJsonParser.ParseLine(await ReadFixtureAsync("event-assistant-text.json")).ToList();

        var text = events.ShouldHaveSingleItem().ShouldBeOfType<PiEvent.AssistantTextEvent>();
        text.Text.ShouldBe("Hello");
    }

    [Fact]
    public async Task RecognizeAssistantToolUseEventAsync()
    {
        var events = StreamJsonParser.ParseLine(await ReadFixtureAsync("event-assistant-tool-use.json")).ToList();

        var toolUse = events.ShouldHaveSingleItem().ShouldBeOfType<PiEvent.AssistantToolUseEvent>();
        toolUse.Tool.ShouldBe("Bash");
        toolUse.InputJson.ShouldContain("ls /work");
    }

    [Fact]
    public async Task RecognizeResultEventAsync()
    {
        var events = StreamJsonParser.ParseLine(await ReadFixtureAsync("event-result.json")).ToList();

        var result = events.ShouldHaveSingleItem().ShouldBeOfType<PiEvent.ResultEvent>();
        result.Subtype.ShouldBe("success");
        result.DurationMs.ShouldBe(1234);
        result.CostUsd.ShouldBe(0.0012m);
        result.Result.ShouldBe("Hello");
    }

    [Fact]
    public async Task YieldUnparseableEventForMalformedJsonAsync()
    {
        var events = StreamJsonParser.ParseLine(await ReadFixtureAsync("event-malformed.txt")).ToList();

        var unparseable = events.ShouldHaveSingleItem().ShouldBeOfType<PiEvent.UnparseableEvent>();
        unparseable.Line.ShouldContain("not valid json");
        unparseable.Error.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task YieldUnknownEventForUnmodelledTypeAsync()
    {
        var events = StreamJsonParser.ParseLine(await ReadFixtureAsync("event-unknown.json")).ToList();

        var unknown = events.ShouldHaveSingleItem().ShouldBeOfType<PiEvent.UnknownEvent>();
        unknown.Type.ShouldBe("some_future_event_type");
        unknown.Raw.ValueKind.ShouldBe(JsonValueKind.Object);
    }

    [Fact]
    public void SkipEmptyAndWhitespaceLines()
    {
        using var reader = new StringReader("\n  \n\t\n");
        var events = StreamJsonParser.Parse(reader).ToList();

        events.ShouldBeEmpty();
    }

    [Fact]
    public async Task StreamOverMultipleLinesAsync()
    {
        using var reader = new StringReader(
            await ReadFixtureAsync("event-system.json") + "\n" +
            await ReadFixtureAsync("event-user.json") + "\n" +
            await ReadFixtureAsync("event-malformed.txt") + "\n" +
            await ReadFixtureAsync("event-result.json") + "\n");

        var events = StreamJsonParser.Parse(reader).ToList();

        events.Count.ShouldBe(4);
        events.Select(static item => item.GetType()).ShouldBe(
        [
            typeof(PiEvent.SystemEvent),
            typeof(PiEvent.UserEvent),
            typeof(PiEvent.UnparseableEvent),
            typeof(PiEvent.ResultEvent),
        ]);
    }
}
