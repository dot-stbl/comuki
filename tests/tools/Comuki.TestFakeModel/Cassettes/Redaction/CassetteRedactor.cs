using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Comuki.TestFakeModel.Cassettes.Redaction;

/// <summary>
/// Redacts a captured upstream response before <see cref="Recording.CassetteRecordingState"/>
/// ever writes it to disk (design.md's "Cassette format and redaction"):
/// walks the JSON tree, keeping only field names on the known allowlist
/// (the union of the Anthropic and OpenAI wire vocabularies this tool's
/// own <c>Anthropic.Wire</c>/<c>OpenAi.Wire</c> types speak — anything
/// else is, by definition, a field this fake doesn't know how to serve
/// back on replay and might be something sensitive), scrubbing any string
/// value that matches <see cref="SecretPatterns"/> along the way.
/// Fail-closed: an unrecognized field name throws
/// <see cref="CassetteRedactionRefusedException"/> rather than passing the
/// value through unredacted.
/// </summary>
public static class CassetteRedactor
{
    /// <summary>Returns a redacted copy of <paramref name="node"/>. Throws <see cref="CassetteRedactionRefusedException"/> on an unclassifiable field.</summary>
    public static JsonElement Redact(JsonElement node)
    {
        var redacted = RedactionWalker.Walk(JsonSerializer.SerializeToNode(node, JsonSerializerOptions.Web), path: "$");
        return JsonSerializer.SerializeToElement(redacted, JsonSerializerOptions.Web);
    }
}

/// <summary>The recursive tree-walk <see cref="CassetteRedactor"/> composes — extracted per class-layout-and-tooling.md §1a.</summary>
file static class RedactionWalker
{
    // Anthropic's non-streaming tool_use.input is the one place the wire shape embeds
    // genuinely arbitrary, tool-defined JSON (a tool's own parameter names — "city",
    // "file_path", whatever the scripted/real tool call declares). Every OTHER object
    // field this fake serializes has a name from its own fixed vocabulary
    // (CassetteFieldAllowlist), so this is the one field whose *contents* get the more
    // permissive WalkOpaque tree-walk instead — same secret-value scrubbing, no
    // key-name allowlist check (there's no fixed vocabulary to check it against).
    private const string OpaquePayloadField = "input";

    /// <summary>The sentinel value every redacted string is replaced with.</summary>
    private const string RedactedPlaceholder = "[redacted]";

    public static JsonNode? Walk(JsonNode? node, string path)
    {
        return node switch
        {
            null => null,
            JsonObject asObject => WalkObject(asObject, path),
            JsonArray asArray => WalkArray(asArray, path),
            JsonValue asValue => WalkValue(asValue),
            _ => throw new CassetteRedactionRefusedException($"{path}: unrecognized JSON node type '{node.GetType()}' — refusing to write."),
        };
    }

    public static JsonObject WalkObject(JsonObject source, string path)
    {
        var result = new JsonObject();
        foreach (var (name, value) in source)
        {
            var childPath = $"{path}.{name}";
            if (SecretPatterns.IsDenylistedFieldName(name))
            {
                result[name] = RedactedPlaceholder;
                continue;
            }

            if (!CassetteFieldAllowlist.Fields.Contains(name))
            {
                throw new CassetteRedactionRefusedException(
                    $"{childPath}: field '{name}' is not on the cassette redaction allowlist — refusing to write an unclassified field.");
            }

            result[name] = name == OpaquePayloadField ? WalkOpaque(value) : Walk(value, childPath);
        }

        return result;
    }

    public static JsonArray WalkArray(JsonArray source, string path)
    {
        var result = new JsonArray();
        for (var index = 0; index < source.Count; index++)
        {
            result.Add(Walk(source[index], $"{path}[{index}]"));
        }

        return result;
    }

    public static JsonValue WalkValue(JsonValue value)
    {
        if (value.TryGetValue<string>(out var text) && SecretPatterns.LooksLikeSecret(text))
        {
            return JsonValue.Create(RedactedPlaceholder);
        }

        // A JsonNode can only ever have one parent: `value` is still attached to the
        // source tree `Redact` deserialized, so re-attaching it under `result` as-is
        // would throw "The node already has a parent." DeepClone() detaches a copy.
        return (JsonValue)value.DeepClone();
    }

    /// <summary>Redacts secret-shaped leaf values without checking any key name against the allowlist — see <see cref="OpaquePayloadField"/>.</summary>
    public static JsonNode? WalkOpaque(JsonNode? node)
    {
        return node switch
        {
            null => null,
            JsonObject asObject => WalkOpaqueObject(asObject),
            JsonArray asArray => WalkOpaqueArray(asArray),
            JsonValue asValue => WalkValue(asValue),
            _ => throw new CassetteRedactionRefusedException($"unrecognized JSON node type '{node.GetType()}' inside a tool-input payload — refusing to write."),
        };
    }

    public static JsonObject WalkOpaqueObject(JsonObject source)
    {
        var result = new JsonObject();
        foreach (var (name, value) in source)
        {
            result[name] = SecretPatterns.IsDenylistedFieldName(name) ? RedactedPlaceholder : WalkOpaque(value);
        }

        return result;
    }

    public static JsonArray WalkOpaqueArray(JsonArray source)
    {
        var result = new JsonArray();
        foreach (var item in source)
        {
            result.Add(WalkOpaque(item));
        }

        return result;
    }
}

/// <summary>
/// The union of every JSON object field name this tool's own
/// <c>Anthropic.Wire</c>/<c>Anthropic.Response</c>/<c>OpenAi.Wire</c>/
/// <c>OpenAi.Response</c> types serialize — the redactor's ground truth
/// for "a field the fake knows how to serve back on replay". Extending
/// either wire shape (a new scripted content-block kind, a new usage
/// field) must extend this set in the same change, or recording against
/// the real upstream will start refusing to write.
/// </summary>
file static class CassetteFieldAllowlist
{
    public static readonly FrozenSet<string> Fields = new[]
    {
        // Shared envelope
        "id", "type", "role", "model", "content", "usage", "index", "name",
        // Anthropic
        "stop_reason", "stop_sequence", "input_tokens", "output_tokens", "text",
        "input", "message", "delta", "partial_json", "content_block",
        // OpenAI
        "object", "created", "choices", "tool_calls", "finish_reason",
        "prompt_tokens", "completion_tokens", "total_tokens", "function", "arguments",
        // Error envelopes (Anthropic.Errors.AnthropicErrorBody / OpenAi.Errors.OpenAiErrorBody)
        "error", "param", "code",
    }.ToFrozenSet(StringComparer.Ordinal);
}
