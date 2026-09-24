using System.Text.Json.Serialization;

namespace Comuki.TestFakeModel.Cassettes;

/// <summary>
/// The on-disk cassette document — one JSON file per scenario per model,
/// versioned (design.md's "Cassette format and redaction"). Exchanges are
/// in call order; replay consumes them sequentially (see
/// <c>Replay.CassettePlaybackState</c>).
/// </summary>
public sealed record CassetteFile(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("scenario")] string Scenario,
    [property: JsonPropertyName("recordedAt")] DateTimeOffset RecordedAt,
    [property: JsonPropertyName("recordedAgainst")] string RecordedAgainst,
    [property: JsonPropertyName("exchanges")] IReadOnlyList<CassetteExchange> Exchanges)
{
    /// <summary>The only schema version this build reads/writes — bump and branch on read when the shape changes.</summary>
    public const int CurrentSchemaVersion = 1;
}
