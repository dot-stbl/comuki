using System.Text.Json.Serialization;
using Comuki.TestFakeModel.Cassettes.Wire;

namespace Comuki.TestFakeModel.Cassettes;

/// <summary>One recorded request/response pair, in call order — the cassette's unit of replay.</summary>
public sealed record CassetteExchange(
    [property: JsonPropertyName("request")] CassetteRequest Request,
    [property: JsonPropertyName("response")] CassetteResponse Response);
