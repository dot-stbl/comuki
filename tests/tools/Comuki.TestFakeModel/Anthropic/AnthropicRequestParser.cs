using System.Text.Json;

namespace Comuki.TestFakeModel.Anthropic;

/// <summary>Extracts an <see cref="ObservedRequest"/> from a parsed Messages API request body.</summary>
public static class AnthropicRequestParser
{
    /// <summary>Parses <paramref name="root"/> — the request body's root JSON element.</summary>
    public static ObservedRequest Parse(JsonElement root)
    {
        var extracted = root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array
            ? AnthropicUserContentExtractor.ExtractLastUserMessage(messages)
            : new ExtractedContent(null, false);

        return new ObservedRequest(
            extracted.Text,
            extracted.HasToolResult,
            root.TryGetProperty("model", out var modelElement) && modelElement.ValueKind == JsonValueKind.String ? modelElement.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("stream", out var streamElement) && streamElement.ValueKind == JsonValueKind.True);
    }
}

/// <summary>A named stand-in for the tuple <see cref="AnthropicUserContentExtractor"/> would otherwise return (naming-and-types.md §1: tuples are banned).</summary>
file sealed record ExtractedContent(string? Text, bool HasToolResult);

/// <summary>The extraction steps <see cref="AnthropicRequestParser"/> composes — extracted per class-layout-and-tooling.md §1a.</summary>
file static class AnthropicUserContentExtractor
{
    /// <summary>
    /// Walks <paramref name="messages"/> from the end for the last
    /// <c>role: "user"</c> entry — the one a tool_result reply (or the
    /// original prompt) would appear in — and extracts its text content
    /// plus whether it carries a <c>tool_result</c> block.
    /// </summary>
    public static ExtractedContent ExtractLastUserMessage(JsonElement messages)
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

        return new ExtractedContent(null, false);
    }

    public static ExtractedContent ExtractContent(JsonElement message)
    {
        if (!message.TryGetProperty("content", out var content))
        {
            return new ExtractedContent(null, false);
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            return new ExtractedContent(content.GetString(), false);
        }

        if (content.ValueKind != JsonValueKind.Array)
        {
            return new ExtractedContent(null, false);
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

        return new ExtractedContent(texts.Count == 0 ? null : string.Join('\n', texts), hasToolResult);
    }
}
