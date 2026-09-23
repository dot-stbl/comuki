using System.Text.Json;
using System.Text.Json.Serialization;

namespace Comuki.TestFakeModel.Anthropic;

/// <summary>The <see cref="JsonSerializerOptions"/> every wire-shape response and SSE event is serialized with.</summary>
internal static class AnthropicJsonOptions
{
    /// <summary>
    /// Property names come entirely from each record's <c>JsonPropertyName</c>
    /// attribute (no naming policy needed); null fields — the ones a
    /// particular content-block/delta variant doesn't use — are omitted
    /// rather than serialized as <c>null</c>.
    /// </summary>
    public static readonly JsonSerializerOptions Default = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
