using System.Text.Json;

namespace Comuki.Host.Translator.Parsing;

/// <summary>
/// Pure parser for <c>pi</c>'s stream-json output: one JSON object per line, no I/O.
/// Tolerates malformed lines (yields <see cref="PiEvent.UnparseableEvent"/>) and unknown
/// event types (yields <see cref="PiEvent.UnknownEvent"/>) so a single bad line cannot
/// kill a running task.
/// </summary>
/// <remarks>
/// Static methods by design — the parser has no state. If we later need to inject a
/// logger or options, switch to instance methods + a primary constructor; the public
/// signatures stay the same.
/// </remarks>
public static class StreamJsonParser
{
    /// <summary>
    /// Parses every non-empty line of <paramref name="reader"/> into a <see cref="PiEvent"/>.
    /// The reader is consumed line-by-line; the caller owns its lifetime and disposal.
    /// </summary>
    /// <param name="reader"></param>
    public static IEnumerable<PiEvent> Parse(TextReader reader)
    {
        while (reader.ReadLine() is { } line)
        {
            foreach (var piEvent in ParseLine(line))
            {
                yield return piEvent;
            }
        }
    }

    /// <summary>
    /// Parses a single line. Exposed separately so tests can target the per-line
    /// contract directly without wrapping strings in a <see cref="StringReader"/>.
    /// </summary>
    /// <param name="line"></param>
    public static IEnumerable<PiEvent> ParseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            yield break;
        }

        var (document, error) = TryParseDocument(line);
        if (document is null)
        {
            yield return new PiEvent.UnparseableEvent(line, error ?? "unknown parse error");
            yield break;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var typeElement)
                || typeElement.ValueKind != JsonValueKind.String)
            {
                yield return new PiEvent.UnparseableEvent(line, "Missing or non-string 'type' field");
                yield break;
            }

            var type = typeElement.GetString()!;
            yield return type switch
            {
                "system" => MapSystem(root),
                "user" => MapUser(root),
                "assistant" => MapAssistant(root),
                "result" => MapResult(root),
                _ => new PiEvent.UnknownEvent(type, root.Clone()),
            };
        }
    }

    /// <summary>
    /// JSON parse helper extracted so the iterator's catch arm does not contain
    /// <c>yield</c> (CS1631 forbids yield inside catch). Returns the parsed document
    /// or an error message.
    /// </summary>
    /// <param name="line"></param>
    private static (JsonDocument? Document, string? Error) TryParseDocument(string line)
    {
        try
        {
            return (JsonDocument.Parse(line), null);
        }
        catch (JsonException exception)
        {
            return (null, exception.Message);
        }
    }

    private static PiEvent MapSystem(JsonElement root)
    {
        var subtype = root.TryGetProperty("subtype", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString()! : "init";
        var cwd = root.TryGetProperty("cwd", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString()! : string.Empty;
        var tools = new List<string>();
        if (root.TryGetProperty("tools", out var t) && t.ValueKind == JsonValueKind.Array)
        {
            foreach (var tool in t.EnumerateArray())
            {
                if (tool.ValueKind == JsonValueKind.String)
                {
                    tools.Add(tool.GetString()!);
                }
            }
        }

        return new PiEvent.SystemEvent(subtype, cwd, tools);
    }

    private static PiEvent MapUser(JsonElement root) => new PiEvent.UserEvent(ExtractUserContent(root));

    private static PiEvent MapAssistant(JsonElement root)
    {
        if (!root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object)
        {
            return new PiEvent.UnknownEvent("assistant", root.Clone());
        }

        if (!message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return new PiEvent.UnknownEvent("assistant", root.Clone());
        }

        foreach (var block in content.EnumerateArray())
        {
            if (block.ValueKind != JsonValueKind.Object
                || !block.TryGetProperty("type", out var blockTypeElement)
                || blockTypeElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            switch (blockTypeElement.GetString())
            {
                case "text" when block.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String:
                    return new PiEvent.AssistantTextEvent(text.GetString()!);
                case "tool_use" when block.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String:
                    return new PiEvent.AssistantToolUseEvent(
                        name.GetString()!,
                        block.TryGetProperty("input", out var input) ? input.GetRawText() : "{}");
            }
        }

        return new PiEvent.UnknownEvent("assistant", root.Clone());
    }

    private static PiEvent MapResult(JsonElement root)
    {
        var subtype = root.TryGetProperty("subtype", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString()! : "success";
        var durationMs = root.TryGetProperty("duration_ms", out var d) && d.ValueKind == JsonValueKind.Number && d.TryGetInt64(out var duration) ? duration : 0L;
        var costUsd = root.TryGetProperty("cost_usd", out var cost) && cost.ValueKind == JsonValueKind.Number ? cost.GetDecimal() : 0m;
        var result = root.TryGetProperty("result", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString()! : string.Empty;
        return new PiEvent.ResultEvent(subtype, durationMs, costUsd, result);
    }

    private static string ExtractUserContent(JsonElement root) =>
        root.TryGetProperty("message", out var message)
            && message.ValueKind == JsonValueKind.Object
            && message.TryGetProperty("content", out var content)
                ? content.ValueKind switch
                {
                    JsonValueKind.String => content.GetString()!,
                    JsonValueKind.Array => ExtractUserContentFromBlocks(content),
                    _ => string.Empty,
                }
            : string.Empty;

    private static string ExtractUserContentFromBlocks(JsonElement blocks)
    {
        var combined = new System.Text.StringBuilder();
        foreach (var block in blocks.EnumerateArray())
        {
            if (block.ValueKind == JsonValueKind.Object
                && block.TryGetProperty("text", out var text)
                && text.ValueKind == JsonValueKind.String)
            {
                combined.Append(text.GetString());
            }
        }

        return combined.ToString();
    }
}
