using System.Text.Json.Serialization;

namespace Comuki.TestFakeModel.Cassettes.Wire;

/// <summary>
/// The recorded request's matching key — design.md's "Cassette format and
/// redaction": <c>matchOn</c> never stores the raw prompt, only a hash and
/// a small structural predicate, so cassettes stay diffable and small
/// without keeping ticket/customer text verbatim in git.
/// </summary>
public sealed record CassetteRequestMatch(
    [property: JsonPropertyName("lastUserMessageHash")] string? LastUserMessageHash,
    [property: JsonPropertyName("toolResultPresent")] bool ToolResultPresent);
