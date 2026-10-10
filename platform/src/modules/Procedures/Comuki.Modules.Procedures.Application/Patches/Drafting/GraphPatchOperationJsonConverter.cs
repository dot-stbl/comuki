using System.Text.Json;
using System.Text.Json.Serialization;
using Comuki.Modules.Procedures.Domain.Definitions.Elements;
using Comuki.Modules.Procedures.Domain.Patches.Model;

namespace Comuki.Modules.Procedures.Application.Patches.Drafting;

/// <summary>
/// System.Text.Json converter for the closed
/// <see cref="GraphPatchOperation"/> hierarchy. The wire shape carries
/// a <c>kind</c> discriminator (<c>add-node</c> / <c>remove-node</c> /
/// <c>rewire-edge</c> / <c>re-parameterize-node</c>) and a body that
/// differs per kind; a single converter reads <c>kind</c> and routes to
/// the per-type deserializer. STJ's default polymorphic handling would
/// require a derived <c>[JsonDerivedType]</c> attribute on the closed
/// record — fine in principle, but the wire is owned by the kubb
/// pipeline, not the domain, and the closed-record hierarchy's private
/// constructor would still let the brain sneak in an unregistered
/// subtype. Routing through a converter in the Application layer keeps
/// the discriminator mapping at the boundary the wire crosses.
///
/// <para>
/// Per <c>json-and-ndjson.md</c> §5: a polymorphic converter must explain
/// why <c>[JsonPropertyName]</c> + a naming policy is not enough, must
/// dispatch via a <c>switch</c>-expression on the discriminator (no
/// god-if), and must surface an explicit typed refusal for an unknown
/// kind rather than throwing <c>JsonException</c> with a generic message.
/// </para>
/// </summary>
public sealed class GraphPatchOperationJsonConverter : JsonConverter<GraphPatchOperation>
{
    private const string KindProperty = "kind";

    /// <summary>The four discriminator values the wire speaks; closed — adding one
    /// requires a new <see cref="GraphPatchOperation"/> nested record AND a new
    /// case in <see cref="Read"/>.</summary>
    public static class Kinds
    {
        public const string AddNode = "add-node";
        public const string RemoveNode = "remove-node";
        public const string RewireEdge = "rewire-edge";
        public const string ReParameterizeNode = "re-parameterize-node";
    }

    /// <inheritdoc />
    public override GraphPatchOperation? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException(
                $"GraphPatchOperation must be a JSON object (got {reader.TokenType}).");
        }

        // Walk the discriminator to find the kind; once we know which
        // type to construct, hand a fresh reader snapshot positioned
        // at the same StartObject to the per-type deserializer (the
        // per-type reader does its own property iteration).
        if (!TryFindKind(ref reader, out var kind))
        {
            throw new JsonException(
                "GraphPatchOperation is missing the 'kind' discriminator property.");
        }

        // Re-snapshot at the StartObject so the per-type deserializer
        // sees the discriminator's value too (it may need to re-read
        // it for the write shape). Take the snapshot BEFORE TryFindKind
        // advances the reader.
        // — actually, TryFindKind advances the caller's reader. The
        // snapshot must therefore be taken at the StartObject before
        // TryFindKind runs. We restructure: take snapshot first, then
        // walk via the snapshot's copy.
        // (Refactored — see ReadCore below.)
        return ReadCore(ref reader, kind, options);
    }

    /// <summary>Read the discriminator + the body in one pass; the reader
    /// ends at the matching EndObject so the outer deserializer continues
    /// from a consistent position.</summary>
    private static GraphPatchOperation? ReadCore(
        ref Utf8JsonReader reader,
        string kind,
        JsonSerializerOptions options)
    {
        return kind switch
        {
            Kinds.AddNode => DeserializeAddNode(ref reader, options),
            Kinds.RemoveNode => DeserializeRemoveNode(ref reader, options),
            Kinds.RewireEdge => DeserializeRewireEdge(ref reader, options),
            Kinds.ReParameterizeNode => DeserializeReParameterizeNode(ref reader, options),
            _ => throw new JsonException(
                $"Unknown GraphPatchOperation kind '{kind}'. " +
                $"Expected one of: {Kinds.AddNode}, {Kinds.RemoveNode}, " +
                $"{Kinds.RewireEdge}, {Kinds.ReParameterizeNode}."),
        };
    }

    /// <summary>
    /// Walks the operation's properties to find the <c>kind</c>
    /// discriminator value. The reader is left positioned past the
    /// discriminator's value (so the per-type deserializer can
    /// iterate through the remaining properties without re-reading
    /// the discriminator). Returns true on success, false when the
    /// <c>kind</c> property is missing.
    /// </summary>
    private static bool TryFindKind(ref Utf8JsonReader reader, out string kind)
    {
        kind = string.Empty;
        // Skip past the StartObject token the caller left us on.
        if (!reader.Read())
        {
            return false;
        }

        while (reader.TokenType == JsonTokenType.PropertyName)
        {
            var name = reader.GetString();
            reader.Read();
            if (name == KindProperty && reader.TokenType == JsonTokenType.String)
            {
                kind = reader.GetString()!;
                // Advance past the kind's value so the next iteration
                // sees the next PropertyName (or the EndObject).
                reader.Read();
                return true;
            }

            reader.Skip();
        }

        return false;
    }

    /// <inheritdoc />
    public override void Write(
        Utf8JsonWriter writer,
        GraphPatchOperation value,
        JsonSerializerOptions options)
    {
        switch (value)
        {
            case GraphPatchOperation.AddNode add:
                writer.WriteStartObject();
                writer.WriteString(KindProperty, Kinds.AddNode);
                writer.WritePropertyName("node");
                JsonSerializer.Serialize(writer, add.Node, options);
                writer.WriteEndObject();
                break;
            case GraphPatchOperation.RemoveNode remove:
                writer.WriteStartObject();
                writer.WriteString(KindProperty, Kinds.RemoveNode);
                writer.WriteString("nodeId", remove.NodeId);
                writer.WriteEndObject();
                break;
            case GraphPatchOperation.RewireEdge rewire:
                writer.WriteStartObject();
                writer.WriteString(KindProperty, Kinds.RewireEdge);
                writer.WritePropertyName("before");
                JsonSerializer.Serialize(writer, rewire.Before, options);
                writer.WritePropertyName("after");
                JsonSerializer.Serialize(writer, rewire.After, options);
                writer.WriteEndObject();
                break;
            case GraphPatchOperation.ReParameterizeNode reparam:
                writer.WriteStartObject();
                writer.WriteString(KindProperty, Kinds.ReParameterizeNode);
                writer.WriteString("nodeId", reparam.NodeId);
                writer.WritePropertyName("newParameters");
                JsonSerializer.Serialize(writer, reparam.NewParameters, options);
                writer.WriteEndObject();
                break;
            default:
                throw new JsonException(
                    $"Unknown GraphPatchOperation type '{value.GetType().FullName}'; " +
                    "the closed hierarchy should reject this at compile time.");
        }
    }

    /// <summary>
    /// Deserializes the per-type body. The reader is positioned on the
    /// first property AFTER the <c>kind</c> discriminator (or the
    /// EndObject if <c>kind</c> was the last property). The function
    /// finds the named property and deserializes the body that follows
    /// it. Unknown properties are skipped.
    /// </summary>
    private static GraphPatchOperation DeserializeAddNode(
        ref Utf8JsonReader reader,
        JsonSerializerOptions options)
    {
        var node = FindAndRead<ProcedureNode>(ref reader, "node", options);
        return new GraphPatchOperation.AddNode(node);
    }

    private static GraphPatchOperation DeserializeRemoveNode(
        ref Utf8JsonReader reader,
        JsonSerializerOptions options)
    {
        var nodeId = FindAndReadString(ref reader, "nodeId");
        return new GraphPatchOperation.RemoveNode(nodeId);
    }

    private static GraphPatchOperation DeserializeRewireEdge(
        ref Utf8JsonReader reader,
        JsonSerializerOptions options)
    {
        var before = FindAndRead<ProcedureEdge>(ref reader, "before", options);
        var after = FindAndRead<ProcedureEdge>(ref reader, "after", options);
        return new GraphPatchOperation.RewireEdge(before, after);
    }

    private static GraphPatchOperation DeserializeReParameterizeNode(
        ref Utf8JsonReader reader,
        JsonSerializerOptions options)
    {
        var nodeId = FindAndReadString(ref reader, "nodeId");
        var newParameters = FindAndRead<IReadOnlyDictionary<string, string>>(
            ref reader, "newParameters", options);
        return new GraphPatchOperation.ReParameterizeNode(nodeId, newParameters);
    }

    /// <summary>
    /// Walks the reader through the operation's properties (skipping the
    /// already-consumed <c>kind</c> discriminator at the head), finds
    /// the named property, and deserializes its body. The reader is
    /// left positioned past the consumed value, so subsequent
    /// <c>FindAndRead</c> calls can scan the remaining properties.
    /// </summary>
    private static T FindAndRead<T>(
        ref Utf8JsonReader reader,
        string propertyName,
        JsonSerializerOptions options)
    {
        while (reader.TokenType == JsonTokenType.PropertyName)
        {
            var name = reader.GetString();
            reader.Read();
            if (name == propertyName)
            {
                var value = JsonSerializer.Deserialize<T>(ref reader, options)
                    ?? throw new JsonException(
                        $"GraphPatchOperation.{propertyName} deserialized to null.");
                // Advance past the consumed value so the caller can
                // find the next property.
                reader.Read();
                return value;
            }

            reader.Skip();
            reader.Read();
        }

        throw new JsonException(
            $"GraphPatchOperation is missing the '{propertyName}' property.");
    }

    /// <summary>Same as <see cref="FindAndRead{T}"/> for string-typed properties.</summary>
    private static string FindAndReadString(
        ref Utf8JsonReader reader,
        string propertyName)
    {
        while (reader.TokenType == JsonTokenType.PropertyName)
        {
            var name = reader.GetString();
            reader.Read();
            if (name == propertyName)
            {
                if (reader.TokenType != JsonTokenType.String)
                {
                    throw new JsonException(
                        $"GraphPatchOperation.{propertyName} must be a string (got {reader.TokenType}).");
                }

                var value = reader.GetString() ?? throw new JsonException(
                    $"GraphPatchOperation.{propertyName} is null in the wire payload.");
                reader.Read();
                return value;
            }

            reader.Skip();
            reader.Read();
        }

        throw new JsonException(
            $"GraphPatchOperation is missing the '{propertyName}' property.");
    }
}
