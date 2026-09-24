using System.Text.Json.Serialization;

namespace Comuki.TestFakeModel.Anthropic.Errors;

/// <summary>The nested <c>error</c> object.</summary>
public sealed record AnthropicErrorDetail(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("message")] string Message);
