using System.Text.Json;
using System.Text.Json.Serialization;

namespace Comuki.TestFakeModel.Cassettes.Wire;

/// <summary>
/// One captured SSE frame. <see cref="Type"/> is the frame's <c>event:</c>
/// line when the upstream sent one (Anthropic's shape); <c>null</c> when it
/// didn't (OpenAI's shape sends bare <c>data:</c> lines). <see cref="Data"/>
/// is always valid JSON — the OpenAI <c>data: [DONE]</c> terminator is
/// represented by <see cref="Done"/> rather than breaking that invariant
/// (a bare <c>[DONE]</c> token isn't valid JSON on its own).
/// </summary>
public sealed record CassetteSseEvent(
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("data")] JsonElement Data)
{
    /// <summary>The literal value <see cref="Data"/> carries for the <c>[DONE]</c> terminator sentinel.</summary>
    public const string DoneMarker = "[DONE]";

    /// <summary>The <c>data: [DONE]</c> terminator, represented as a sentinel event rather than invalid JSON.</summary>
    public static CassetteSseEvent Done { get; } = new(Type: null, JsonSerializer.SerializeToElement(DoneMarker));

    /// <summary>True when this event is the <c>[DONE]</c> terminator sentinel. Computed, not part of the wire shape.</summary>
    [JsonIgnore]
    public bool IsDone => Type is null && Data.ValueKind == JsonValueKind.String && Data.GetString() == DoneMarker;
}
