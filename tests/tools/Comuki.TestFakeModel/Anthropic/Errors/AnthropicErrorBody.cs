using System.Text.Json.Serialization;

namespace Comuki.TestFakeModel.Anthropic.Errors;

/// <summary>The Anthropic error envelope.</summary>
public sealed record AnthropicErrorBody(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("error")] AnthropicErrorDetail Error);
