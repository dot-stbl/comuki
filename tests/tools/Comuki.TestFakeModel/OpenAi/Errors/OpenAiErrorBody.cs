using System.Text.Json.Serialization;

namespace Comuki.TestFakeModel.OpenAi.Errors;

/// <summary>The OpenAI error envelope — <c>{"error": {...}}</c>.</summary>
public sealed record OpenAiErrorBody([property: JsonPropertyName("error")] OpenAiErrorDetail Error);
