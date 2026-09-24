using System.Text.Json;
using System.Text.Json.Serialization;

namespace Comuki.TestFakeModel.Cassettes.Wire;

/// <summary>
/// The recorded response — <see cref="Body"/> for a non-streamed exchange,
/// <see cref="Events"/> for a streamed one (exactly one of the two is
/// non-null, matching <see cref="Streamed"/>). No header is ever
/// persisted, not even <c>content-type</c>: redaction's "every header is
/// stripped except content-type" (design.md) is satisfied a fortiori by
/// never writing headers at all — replay mode derives the response's
/// <c>Content-Type</c> from <see cref="Streamed"/> the same way fake mode
/// already does, so there was nothing a kept header would add.
/// </summary>
public sealed record CassetteResponse(
    [property: JsonPropertyName("status")] int Status,
    [property: JsonPropertyName("streamed")] bool Streamed,
    [property: JsonPropertyName("body")] JsonElement? Body,
    [property: JsonPropertyName("events")] IReadOnlyList<CassetteSseEvent>? Events);
