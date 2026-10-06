using System.Text.Json;
using System.Text.Json.Serialization;

namespace Comuki.Shared.Contracts.Verification;

/// <summary>
/// System.Text.Json converter for the closed <see cref="GateEvidenceKind"/>
/// value (add-orchestra §3 — Coda, <c>verification/spec.md</c> "Bundle
/// accepts text/x-diff evidence"). Wire form is the lowercase, dot.case
/// value <see cref="GateEvidenceKind.Value"/> exposes; <see cref="GateEvidenceKind.FromWire"/>
/// narrows an unknown wire value to <see cref="GateEvidenceKind.Other"/>
/// so a freshly-loaded <c>evidence.kind</c> never throws on a kind the
/// catalogue has not seen yet (the kind set is open per the journal
/// open-type rule — provider-specific kinds collapse to <c>other</c>
/// on read, never to a parse error).
/// </summary>
public sealed class GateEvidenceKindJsonConverter : JsonConverter<GateEvidenceKind>
{
    /// <inheritdoc />
    public override GateEvidenceKind Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return default;
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException(
                $"GateEvidenceKind must be a JSON string (got {reader.TokenType}).");
        }

        var wire = reader.GetString();
        return GateEvidenceKind.FromWire(wire);
    }

    /// <inheritdoc />
    public override void Write(
        Utf8JsonWriter writer,
        GateEvidenceKind value,
        JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}

/// <summary>
/// System.Text.Json converter for the closed <see cref="GateVerdict"/>
/// value (add-orchestra §3 — Coda, <c>verification/spec.md</c> "VerificationRecord
/// is a per-WorkItem sibling table"). Wire form is the lowercase
/// <see cref="GateVerdict.Value"/>; <see cref="GateVerdict.FromWire"/>
/// collapses an unknown wire value to <see cref="GateVerdict.Unspecified"/>
/// so a freshly-loaded row whose column default is the canonical
/// <c>"pending"</c> string round-trips without throwing.
/// </summary>
public sealed class GateVerdictJsonConverter : JsonConverter<GateVerdict>
{
    /// <inheritdoc />
    public override GateVerdict Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return default;
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException(
                $"GateVerdict must be a JSON string (got {reader.TokenType}).");
        }

        var wire = reader.GetString();
        return GateVerdict.FromWire(wire);
    }

    /// <inheritdoc />
    public override void Write(
        Utf8JsonWriter writer,
        GateVerdict value,
        JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}
