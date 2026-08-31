using Comuki.Host.Translator.Parsing;
using Shouldly;
using Xunit;

namespace Comuki.Host.Translator.Unit.StreamJson;

/// <summary>
/// Unit tests for the pi-native json-mode event types (pi 0.84.x
/// <c>--mode json</c>): the session header, message_update deltas and
/// tool calls, tool_execution_start, authoritative message_end text and
/// agent_end. Shapes recorded from the pi docs and the T3.0 sanity probe.
/// </summary>
public sealed class PiNativeStreamJsonParserShould
{
    private static async Task<string> ReadFixtureAsync(string fileName)
    {
        return await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RecognizeSessionHeaderAsync()
    {
        var events = StreamJsonParser.ParseLine(await ReadFixtureAsync("pi-session.json")).ToList();

        var session = events.ShouldHaveSingleItem().ShouldBeOfType<PiEvent.SessionHeaderEvent>();
        session.Version.ShouldBe(3);
        session.SessionId.ShouldBe("01a057b8-b6f4-76f4-81d2-88803a817922");
        session.Cwd.ShouldBe("/work");
    }

    [Fact]
    public async Task RecognizeTextDeltaAsync()
    {
        var events = StreamJsonParser.ParseLine(await ReadFixtureAsync("pi-message-update-text-delta.json")).ToList();

        var delta = events.ShouldHaveSingleItem().ShouldBeOfType<PiEvent.TextDeltaEvent>();
        delta.ContentIndex.ShouldBe(0);
        delta.Delta.ShouldBe("Hello");
    }

    [Fact]
    public async Task RecognizeToolcallStartAsync()
    {
        var events = StreamJsonParser.ParseLine(await ReadFixtureAsync("pi-message-update-toolcall-start.json")).ToList();

        var toolCall = events.ShouldHaveSingleItem().ShouldBeOfType<PiEvent.ToolCallEvent>();
        toolCall.ToolName.ShouldBe("Bash");
        toolCall.ArgsJson.ShouldContain("ls /work");
    }

    [Fact]
    public async Task RecognizeToolExecutionStartAsync()
    {
        var events = StreamJsonParser.ParseLine(await ReadFixtureAsync("pi-tool-execution-start.json")).ToList();

        var toolCall = events.ShouldHaveSingleItem().ShouldBeOfType<PiEvent.ToolCallEvent>();
        toolCall.ToolName.ShouldBe("Bash");
        toolCall.ArgsJson.ShouldContain("ls /work");
    }

    [Fact]
    public async Task RecognizeAuthoritativeMessageEndTextAsync()
    {
        var events = StreamJsonParser.ParseLine(await ReadFixtureAsync("pi-message-end.json")).ToList();

        var text = events.ShouldHaveSingleItem().ShouldBeOfType<PiEvent.AssistantTextEvent>();
        text.Text.ShouldBe("Hello world");
    }

    [Fact]
    public async Task RecognizeAgentEndAsync()
    {
        var events = StreamJsonParser.ParseLine(await ReadFixtureAsync("pi-agent-end.json")).ToList();

        _ = events.ShouldHaveSingleItem().ShouldBeOfType<PiEvent.AgentEndEvent>();
    }

    [Fact]
    public async Task SurfaceUnmodelledMessageUpdateAsUnknownAsync()
    {
        var events = StreamJsonParser.ParseLine(
            """{"type":"message_update","usage":{},"assistantMessageEvent":{"type":"thinking_delta","delta":"..."}}""").ToList();

        var unknown = events.ShouldHaveSingleItem().ShouldBeOfType<PiEvent.UnknownEvent>();
        unknown.Type.ShouldBe("message_update");
    }
}
