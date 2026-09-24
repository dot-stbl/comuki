using System.Text.Json;

namespace Comuki.TestFakeModel.OpenAi;

/// <summary>
/// Extracts an <see cref="ObservedRequest"/> from a parsed OpenAI Chat
/// Completions request body. Same target shape as
/// <c>Anthropic.AnthropicRequestParser</c> — the fakeScript matcher and
/// recorded-request log don't care which protocol produced it — but the
/// extraction differs where the two wire shapes diverge: OpenAI reports a
/// tool result as a separate <c>{"role":"tool", ...}</c> message rather
/// than a <c>tool_result</c> block nested in the user message, so
/// <see cref="ObservedRequest.HasToolResult"/> here means "any message in
/// the array has <c>role: tool</c>", not "the last user message has one".
/// </summary>
public static class OpenAiRequestParser
{
    /// <summary>Parses <paramref name="root"/> — the request body's root JSON element.</summary>
    public static ObservedRequest Parse(JsonElement root)
    {
        var hasMessages = root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array;

        return new ObservedRequest(
            hasMessages ? OpenAiUserContentExtractor.LastUserMessageText(messages) : null,
            hasMessages && OpenAiUserContentExtractor.HasToolMessage(messages),
            root.TryGetProperty("model", out var modelElement) && modelElement.ValueKind == JsonValueKind.String ? modelElement.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("stream", out var streamElement) && streamElement.ValueKind == JsonValueKind.True);
    }
}

/// <summary>The extraction steps <see cref="OpenAiRequestParser"/> composes — extracted per class-layout-and-tooling.md §1a.</summary>
file static class OpenAiUserContentExtractor
{
    /// <summary>True when any message in <paramref name="messages"/> carries <c>role: "tool"</c> — OpenAI's tool-result message shape.</summary>
    public static bool HasToolMessage(JsonElement messages)
    {
        foreach (var message in messages.EnumerateArray())
        {
            if (message.TryGetProperty("role", out var roleElement) && roleElement.GetString() == "tool")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Walks <paramref name="messages"/> from the end for the last <c>role: "user"</c> entry and extracts its text content.</summary>
    public static string? LastUserMessageText(JsonElement messages)
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

        return null;
    }

    /// <summary><c>content</c> is a plain string, or (OpenAI's multimodal shape) an array of <c>{"type":"text","text":"..."}</c>-and-other parts.</summary>
    public static string? ExtractContent(JsonElement message)
    {
        if (!message.TryGetProperty("content", out var content))
        {
            return null;
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString();
        }

        if (content.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var texts = new List<string>();
        foreach (var part in content.EnumerateArray())
        {
            if (part.TryGetProperty("type", out var typeElement)
                && typeElement.GetString() == "text"
                && part.TryGetProperty("text", out var textElement))
            {
                texts.Add(textElement.GetString() ?? string.Empty);
            }
        }

        return texts.Count == 0 ? null : string.Join('\n', texts);
    }
}
