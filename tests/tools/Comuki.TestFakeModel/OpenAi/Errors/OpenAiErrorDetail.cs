using System.Text.Json.Serialization;

namespace Comuki.TestFakeModel.OpenAi.Errors;

/// <summary>The nested <c>error</c> object — OpenAI's envelope has no top-level <c>type</c> field (unlike Anthropic's), only this.</summary>
public sealed record OpenAiErrorDetail(
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("param")] string? Param,
    [property: JsonPropertyName("code")] string? Code);
