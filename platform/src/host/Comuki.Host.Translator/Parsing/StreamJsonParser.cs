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

        var attempt = StreamJsonEventMapping.TryParseDocument(line);
        if (attempt is not { Document: { } document })
        {
            yield return new PiEvent.UnparseableEvent(line, attempt.Error ?? "unknown parse error");
            yield break;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty(StreamJsonWire.TypeField, out var typeElement)
                || typeElement.ValueKind != JsonValueKind.String
                || typeElement.GetString() is not { } type)
            {
                yield return new PiEvent.UnparseableEvent(line, "Missing or non-string 'type' field");
                yield break;
            }

            yield return type switch
            {
                StreamJsonWire.SystemType => StreamJsonEventMapping.MapSystem(root),
                StreamJsonWire.UserType => StreamJsonEventMapping.MapUser(root),
                StreamJsonWire.AssistantType => StreamJsonEventMapping.MapAssistant(root),
                StreamJsonWire.ResultType => StreamJsonEventMapping.MapResult(root),
                StreamJsonWire.SessionType => StreamJsonEventMapping.MapSessionHeader(root),
                StreamJsonWire.MessageUpdateType => StreamJsonEventMapping.MapMessageUpdate(root),
                StreamJsonWire.MessageEndType => StreamJsonEventMapping.MapAssistant(root),
                StreamJsonWire.ToolExecutionStartType => StreamJsonEventMapping.MapToolExecutionStart(root),
                StreamJsonWire.AgentEndType => new PiEvent.AgentEndEvent(),
                _ => new PiEvent.UnknownEvent(type, root.Clone()),
            };
        }
    }
}

/// <summary>Wire-format names of the pi stream-json convention.</summary>
file static class StreamJsonWire
{
    public const string TypeField = "type";
    public const string SubtypeField = "subtype";
    public const string CwdField = "cwd";
    public const string ToolsField = "tools";
    public const string MessageField = "message";
    public const string ContentField = "content";
    public const string TextField = "text";
    public const string NameField = "name";
    public const string InputField = "input";
    public const string DurationMsField = "duration_ms";
    public const string CostUsdField = "cost_usd";
    public const string ResultField = "result";

    public const string SystemType = "system";
    public const string UserType = "user";
    public const string AssistantType = "assistant";
    public const string ResultType = "result";

    // pi-native json-mode event types (pi --mode json; see the pi docs/json.md).
    public const string SessionType = "session";
    public const string MessageUpdateType = "message_update";
    public const string MessageEndType = "message_end";
    public const string ToolExecutionStartType = "tool_execution_start";
    public const string AgentEndType = "agent_end";

    public const string VersionField = "version";
    public const string IdField = "id";
    public const string AssistantMessageEventField = "assistantMessageEvent";
    public const string ContentIndexField = "contentIndex";
    public const string DeltaField = "delta";
    public const string ToolNameField = "toolName";
    public const string ArgsField = "args";

    public const string TextBlock = "text";
    public const string ToolUseBlock = "tool_use";

    public const string TextDeltaType = "text_delta";
    public const string ToolCallStartType = "toolcall_start";
}

/// <summary>Result of a single-line JSON parse attempt: a document or an error, never both.</summary>
/// <param name="Document"></param>
/// <param name="Error"></param>
file sealed record StreamJsonParseAttempt(JsonDocument? Document, string? Error);

/// <summary>Tolerant typed accessors over <see cref="JsonElement"/> properties.</summary>
file static class StreamJsonElementExtensions
{
    public static string GetStringOr(this JsonElement element, string propertyName, string fallback)
    {
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            && property.GetString() is { } value
                ? value
                : fallback;
    }

    public static long GetInt64Or(this JsonElement element, string propertyName, long fallback)
    {
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt64(out var value)
                ? value
                : fallback;
    }

    public static decimal GetDecimalOr(this JsonElement element, string propertyName, decimal fallback)
    {
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.GetDecimal() is var value
                ? value
                : fallback;
    }

    public static int GetInt32Or(this JsonElement element, string propertyName, int fallback)
    {
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out var value)
                ? value
                : fallback;
    }
}

/// <summary>Mapping of parsed stream-json documents to <see cref="PiEvent"/> records.</summary>
file static class StreamJsonEventMapping
{
    /// <summary>
    /// JSON parse helper extracted so the caller's iterator has no catch arm around
    /// <c>yield</c> (CS1631 forbids yield inside catch). Returns the parsed document
    /// or an error message.
    /// </summary>
    public static StreamJsonParseAttempt TryParseDocument(string line)
    {
        try
        {
            return new StreamJsonParseAttempt(JsonDocument.Parse(line), null);
        }
        catch (JsonException exception)
        {
            return new StreamJsonParseAttempt(null, exception.Message);
        }
    }

    public static PiEvent MapSystem(JsonElement root)
    {
        var subtype = root.GetStringOr(StreamJsonWire.SubtypeField, "init");
        var cwd = root.GetStringOr(StreamJsonWire.CwdField, string.Empty);
        var tools = new List<string>();
        if (root.TryGetProperty(StreamJsonWire.ToolsField, out var toolsElement)
            && toolsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var tool in toolsElement.EnumerateArray())
            {
                if (tool.ValueKind == JsonValueKind.String && tool.GetString() is { } toolName)
                {
                    tools.Add(toolName);
                }
            }
        }

        return new PiEvent.SystemEvent(subtype, cwd, tools);
    }

    public static PiEvent MapUser(JsonElement root)
    {
        return new PiEvent.UserEvent(ExtractUserContent(root));
    }

    public static PiEvent MapAssistant(JsonElement root)
    {
        if (!root.TryGetProperty(StreamJsonWire.MessageField, out var message)
            || message.ValueKind != JsonValueKind.Object)
        {
            return new PiEvent.UnknownEvent(StreamJsonWire.AssistantType, root.Clone());
        }

        if (!message.TryGetProperty(StreamJsonWire.ContentField, out var content)
            || content.ValueKind != JsonValueKind.Array)
        {
            return new PiEvent.UnknownEvent(StreamJsonWire.AssistantType, root.Clone());
        }

        foreach (var block in content.EnumerateArray())
        {
            if (block.ValueKind != JsonValueKind.Object
                || !block.TryGetProperty(StreamJsonWire.TypeField, out var blockTypeElement)
                || blockTypeElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            switch (blockTypeElement.GetString())
            {
                case StreamJsonWire.TextBlock
                    when block.TryGetProperty(StreamJsonWire.TextField, out var textElement)
                        && textElement.ValueKind == JsonValueKind.String
                        && textElement.GetString() is { } text:
                    return new PiEvent.AssistantTextEvent(text);
                case StreamJsonWire.ToolUseBlock
                    when block.TryGetProperty(StreamJsonWire.NameField, out var nameElement)
                        && nameElement.ValueKind == JsonValueKind.String
                        && nameElement.GetString() is { } name:
                    return new PiEvent.AssistantToolUseEvent(
                        name,
                        block.TryGetProperty(StreamJsonWire.InputField, out var inputElement)
                            ? inputElement.GetRawText()
                            : "{}");
            }
        }

        return new PiEvent.UnknownEvent(StreamJsonWire.AssistantType, root.Clone());
    }

    public static PiEvent MapResult(JsonElement root)
    {
        var subtype = root.GetStringOr(StreamJsonWire.SubtypeField, "success");
        var durationMs = root.GetInt64Or(StreamJsonWire.DurationMsField, 0L);
        var costUsd = root.GetDecimalOr(StreamJsonWire.CostUsdField, 0m);
        var result = root.GetStringOr(StreamJsonWire.ResultField, string.Empty);
        return new PiEvent.ResultEvent(subtype, durationMs, costUsd, result);
    }

    public static PiEvent MapSessionHeader(JsonElement root)
    {
        var version = root.GetInt32Or(StreamJsonWire.VersionField, 0);
        var sessionId = root.GetStringOr(StreamJsonWire.IdField, string.Empty);
        var cwd = root.GetStringOr(StreamJsonWire.CwdField, string.Empty);
        return new PiEvent.SessionHeaderEvent(version, sessionId, cwd);
    }

    public static PiEvent MapMessageUpdate(JsonElement root)
    {
        return root.TryGetProperty(StreamJsonWire.AssistantMessageEventField, out var assistantEvent)
            && assistantEvent.ValueKind == JsonValueKind.Object
            && assistantEvent.TryGetProperty(StreamJsonWire.TypeField, out var typeElement)
            && typeElement.GetString() is { } assistantEventType
                ? MapAssistantMessageEvent(assistantEvent, assistantEventType, root)
                : new PiEvent.UnknownEvent(StreamJsonWire.MessageUpdateType, root.Clone());
    }

    public static PiEvent MapAssistantMessageEvent(JsonElement assistantEvent, string assistantEventType, JsonElement root)
    {
        return assistantEventType switch
        {
            StreamJsonWire.TextDeltaType => new PiEvent.TextDeltaEvent(
                assistantEvent.GetInt32Or(StreamJsonWire.ContentIndexField, 0),
                assistantEvent.GetStringOr(StreamJsonWire.DeltaField, string.Empty)),
            StreamJsonWire.ToolCallStartType => new PiEvent.ToolCallEvent(
                assistantEvent.GetStringOr(StreamJsonWire.ToolNameField, string.Empty),
                assistantEvent.TryGetProperty(StreamJsonWire.ArgsField, out var args)
                    ? args.GetRawText()
                    : "{}"),
            _ => new PiEvent.UnknownEvent(StreamJsonWire.MessageUpdateType, root.Clone()),
        };
    }

    public static PiEvent MapToolExecutionStart(JsonElement root)
    {
        return new PiEvent.ToolCallEvent(
            root.GetStringOr(StreamJsonWire.ToolNameField, string.Empty),
            root.TryGetProperty(StreamJsonWire.ArgsField, out var args) ? args.GetRawText() : "{}");
    }

    public static string ExtractUserContent(JsonElement root)
    {
        return root.TryGetProperty(StreamJsonWire.MessageField, out var message)
            && message.ValueKind == JsonValueKind.Object
            && message.TryGetProperty(StreamJsonWire.ContentField, out var content)
                ? content.ValueKind switch
                {
                    JsonValueKind.String => content.GetString() ?? string.Empty,
                    JsonValueKind.Array => ExtractUserContentFromBlocks(content),
                    _ => string.Empty,
                }
                : string.Empty;
    }

    public static string ExtractUserContentFromBlocks(JsonElement blocks)
    {
        var combined = string.Empty;
        foreach (var block in blocks.EnumerateArray())
        {
            if (block.ValueKind == JsonValueKind.Object
                && block.TryGetProperty(StreamJsonWire.TextField, out var textElement)
                && textElement.ValueKind == JsonValueKind.String)
            {
                combined += textElement.GetString();
            }
        }

        return combined;
    }
}
