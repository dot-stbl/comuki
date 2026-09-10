using System.Text.Json;

namespace Comuki.Modules.Artifacts.Application.VisualArtifacts;

/// <summary>
/// Wire-format payload for <see cref="VisualArtifactEvents.Published"/>.
/// Hand-serialised JSON; the run journal carries an opaque payload
/// string, the dashboard consumes this shape. Property names are
/// camelCase so the FE does not need a separate deserialiser config.
/// </summary>
public static class RunJournalPayloads
{
    /// <summary>One published visual-artifact event.</summary>
    public sealed record ArtifactPublishedPayload(
        Guid ArtifactId,
        string ContentType,
        string Filename,
        long SizeBytes);

    /// <summary>Serialises an <see cref="ArtifactPublishedPayload"/> to compact JSON for the journal.</summary>
    /// <param name="payload"></param>
    public static string SerialiseArtifactPublished(ArtifactPublishedPayload payload)
    {
        return JsonSerializer.Serialize(payload, JsonSerializerOptions.Web);
    }
}
