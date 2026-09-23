using System.Text.Json;

namespace Comuki.TestFakeModel.Anthropic;

/// <summary>
/// The parts of an inbound <c>POST /v1/messages</c> request the fakeScript
/// matcher and the recorded-request log care about. Deliberately not a
/// full typed mirror of the Anthropic Messages request — <c>messages[]</c>
/// content is a polymorphic union (plain string or a block array mixing
/// text/tool_use/tool_result/image/document), so
/// <see cref="AnthropicRequestParser"/> reads it with
/// <see cref="JsonDocument"/> the same way
/// <c>Comuki.Modules.Proxy.Application.Extraction.AnthropicUsageExtractor</c>
/// reads the response shape.
/// </summary>
public sealed record ObservedRequest(string? LastUserMessageText, bool HasToolResult, string Model, bool Stream);

/// <summary>Extracts an <see cref="ObservedRequest"/> from a parsed Messages API request body.</summary>
public static class AnthropicRequestParser
{
    /// <summary>Parses <paramref name="root"/> — the request body's root JSON element.</summary>
    public static ObservedRequest Parse(JsonElement root)
    {
        var model = root.TryGetProperty("model", out var modelElement) && modelElement.ValueKind == JsonValueKind.String
            ? modelElement.GetString() ?? string.Empty
            : string.Empty;

        var stream = root.TryGetProperty("stream", out var streamElement) && streamElement.ValueKind == JsonValueKind.True;

        var (lastUserMessageText, hasToolResult) = root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array
            ? ExtractLastUserMessage(messages)
            : (null, false);

        return new ObservedRequest(lastUserMessageText, hasToolResult, model, stream);
    }

    /// <summary>
    /// Walks <paramref name="messages"/> from the end for the last
    /// <c>role: "user"</c> entry — the one a tool_result reply (or the
    /// original prompt) would appear in — and extracts its text content
    /// plus whether it carries a <c>tool_result</c> block.
    /// </summary>
    private static (string? Text, bool HasToolResult) ExtractLastUserMessage(JsonElement messages)
    {
        for (var index = messages.GetArrayLength() - 1; index >= 0; index--)
        {
            var message = messages[index];
            if (!message.TryGetProperty("role", out var roleElement) || roleElement.GetString() != "user")
            {
                continue;
            }

            return ExtractContent(message);
        }

        return (null, false);
    }

    private static (string? Text, bool HasToolResult) ExtractContent(JsonElement message)
    {
        if (!message.TryGetProperty("content", out var content))
        {
            return (null, false);
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            return (content.GetString(), false);
        }

        if (content.ValueKind != JsonValueKind.Array)
        {
            return (null, false);
        }

        var texts = new List<string>();
        var hasToolResult = false;
        foreach (var block in content.EnumerateArray())
        {
            if (!block.TryGetProperty("type", out var typeElement))
            {
                continue;
            }

            switch (typeElement.GetString())
            {
                case "text" when block.TryGetProperty("text", out var textElement):
                    texts.Add(textElement.GetString() ?? string.Empty);
                    break;
                case "tool_result":
                    hasToolResult = true;
                    break;
            }
        }

        var text = texts.Count == 0 ? null : string.Join('\n', texts);
        return (text, hasToolResult);
    }
}
