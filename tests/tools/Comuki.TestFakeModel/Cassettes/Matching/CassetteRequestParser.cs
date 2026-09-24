using System.Text.Json;
using Comuki.TestFakeModel.Anthropic;
using Comuki.TestFakeModel.OpenAi;

namespace Comuki.TestFakeModel.Cassettes.Matching;

/// <summary>
/// Dispatches request parsing to the right protocol parser by route path —
/// both <c>replay</c> and <c>record</c> mode need an <see cref="ObservedRequest"/>
/// purely to compute the cassette match key (<see cref="CassetteMatchKeyBuilder"/>),
/// regardless of which wire shape the caller is speaking.
/// </summary>
public static class CassetteRequestParser
{
    /// <summary>Parses <paramref name="root"/> using the parser for <paramref name="path"/> — <see cref="OpenAiChatCompletionsEndpoint.RoutePath"/> selects the OpenAI parser, everything else the Anthropic one.</summary>
    public static ObservedRequest Parse(string path, JsonElement root)
    {
        return path == OpenAiChatCompletionsEndpoint.RoutePath
            ? OpenAiRequestParser.Parse(root)
            : AnthropicRequestParser.Parse(root);
    }
}
