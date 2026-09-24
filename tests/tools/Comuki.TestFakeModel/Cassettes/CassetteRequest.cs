using System.Text.Json.Serialization;
using Comuki.TestFakeModel.Cassettes.Wire;

namespace Comuki.TestFakeModel.Cassettes;

/// <summary>The recorded request side of one <see cref="CassetteExchange"/> — never the raw body, only <see cref="MatchOn"/>'s hash/predicate.</summary>
public sealed record CassetteRequest(
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("matchOn")] CassetteRequestMatch MatchOn);
