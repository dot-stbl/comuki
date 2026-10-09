using System.Text.Json.Serialization;

namespace Comuki.Host.Work;

/// <summary>Per-source-ref wire projection.</summary>
public sealed partial record WorkTaskSourceRefView(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("externalId")] string ExternalId,
    [property: JsonPropertyName("displayName")] string? DisplayName);
