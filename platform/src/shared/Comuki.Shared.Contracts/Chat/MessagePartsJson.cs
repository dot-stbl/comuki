using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Comuki.Shared.Contracts.Chat;

/// <summary>
/// Parse/serialize bridge for the <c>parts</c> and <c>meta</c> columns of
/// a transcript row. Both are stored as jsonb and both are optional, so
/// every read is tolerant: a malformed or absent payload reads as "no
/// parts", never as a fault — the flat <c>content</c> projection is always
/// there to fall back on. Uses the frozen
/// <see cref="JsonSerializerOptions.Web"/> (no hand-rolled options).
/// </summary>
public static class MessagePartsJson
{
    /// <summary>Serializes the part list in the canonical camelCase wire form.</summary>
    /// <param name="parts">Ordered parts of one message.</param>
    public static string Serialize(IReadOnlyList<MessagePart> parts)
    {
        return JsonSerializer.Serialize(parts, JsonSerializerOptions.Web);
    }

    /// <summary>Serializes the per-message metadata in the canonical camelCase wire form.</summary>
    /// <param name="meta">Metadata of one message.</param>
    public static string SerializeMeta(ChatMessageMeta meta)
    {
        return JsonSerializer.Serialize(meta, JsonSerializerOptions.Web);
    }

    /// <summary>
    /// Reads a stored part array. Null, empty and malformed payloads all
    /// answer false with a null result — the caller renders
    /// <c>content</c> instead.
    /// </summary>
    /// <param name="json">Stored <c>parts</c> payload.</param>
    /// <param name="parts">The parsed parts; non-null exactly when the return is true.</param>
    public static bool TryParse(string? json, [NotNullWhen(true)] out IReadOnlyList<MessagePart>? parts)
    {
        parts = MessagePartsReading.Parts(json);
        return parts is not null;
    }

    /// <summary>Reads stored metadata; null, empty and malformed payloads answer false.</summary>
    /// <param name="json">Stored <c>meta</c> payload.</param>
    /// <param name="meta">The parsed metadata; non-null exactly when the return is true.</param>
    public static bool TryParseMeta(string? json, [NotNullWhen(true)] out ChatMessageMeta? meta)
    {
        meta = MessagePartsReading.Meta(json);
        return meta is not null;
    }
}

/// <summary>Tolerant deserialization of the two jsonb payloads.</summary>
file static class MessagePartsReading
{
    /// <summary>Deserializes a part array; null on absent or malformed input.</summary>
    /// <param name="json">Stored <c>parts</c> payload.</param>
    public static IReadOnlyList<MessagePart>? Parts(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<IReadOnlyList<MessagePart>>(json, JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            // Unknown "kind" discriminator — a row written by a newer
            // producer. The flat content projection still reads.
            return null;
        }
    }

    /// <summary>Deserializes message metadata; null on absent or malformed input.</summary>
    /// <param name="json">Stored <c>meta</c> payload.</param>
    public static ChatMessageMeta? Meta(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ChatMessageMeta>(json, JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
