using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Comuki.AgentEval.Judges;

/// <summary>
/// Production <see cref="ILlmJudgeClient"/>: POSTs to
/// <c>{COMUKI_LIVE_MODEL_BASE_URL}/v1/messages</c> with the Anthropic
/// Messages wire shape (the same shape
/// <c>Comuki.TestFakeModel.Anthropic.AnthropicMessagesEndpoint</c> speaks
/// — so a scripted FakeModelServer can stand in for any manual smoke
/// test). Auth header is whichever the upstream accepts: <c>x-api-key</c>
/// when the upstream is the hapy gateway, <c>Authorization: Bearer</c>
/// when it's an OpenAI-compatible service. Both are sent; the receiving
/// server picks.
/// </summary>
/// <remarks>
/// Only ever instantiated by <c>Program.cs</c>, only when
/// <c>COMUKI_LIVE_MODEL_BASE_URL</c> is set. Tests inject a fake — never
/// a real HTTP client, per the gate 6 "NEVER a real HTTP client" rule.
/// </remarks>
public sealed class HapyLlmJudgeClient : ILlmJudgeClient, IAsyncDisposable
{
    private static readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient httpClient;
    private readonly Uri endpoint;
    private readonly string? token;

    /// <summary>Builds the client for <paramref name="baseUrl"/> (no trailing slash) with an optional bearer token.</summary>
    /// <param name="baseUrl">The upstream's base URL; the client sends requests to <c>{baseUrl}/v1/messages</c>.</param>
    /// <param name="token">Optional API key or bearer token; null/empty means no auth header is sent.</param>
    public HapyLlmJudgeClient(Uri baseUrl, string? token)
    {
        endpoint = new Uri(baseUrl, "/v1/messages");
        httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        this.token = string.IsNullOrWhiteSpace(token) ? null : token;
    }

    /// <inheritdoc />
    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct)
    {
        var payload = new
        {
            model = "judge",
            max_tokens = 1024,
            stream = false,
            system = systemPrompt,
            messages = new[]
            {
                new { role = "user", content = userPrompt },
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, jsonOptions), Encoding.UTF8, "application/json"),
        };

        if (token is not null)
        {
            request.Headers.Add("x-api-key", token);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await httpClient.SendAsync(request, ct);
        var rawBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"judge upstream returned {(int)response.StatusCode}: {TrimForException(rawBody)}");
        }

        // Anthropic non-streaming Messages response shape:
        //   { "content": [ { "type": "text", "text": "..." } | ... ] }
        // We only care about the first text block — judges emit JSON-only output
        // and we just want the raw string to feed into JudgeVerdictParser.
        using var document = JsonDocument.Parse(rawBody);
        if (!document.RootElement.TryGetProperty("content", out var contentElement)
            || contentElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                $"judge upstream response is missing a 'content' array: {TrimForException(rawBody)}");
        }

        foreach (var block in contentElement.EnumerateArray())
        {
            if (block.ValueKind == JsonValueKind.Object
                && block.TryGetProperty("type", out var typeElement)
                && typeElement.ValueKind == JsonValueKind.String
                && typeElement.GetString() == "text"
                && block.TryGetProperty("text", out var textElement)
                && textElement.ValueKind == JsonValueKind.String)
            {
                return textElement.GetString() ?? string.Empty;
            }
        }

        throw new InvalidOperationException(
            $"judge upstream response contained no text block: {TrimForException(rawBody)}");
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        httpClient.Dispose();
        return ValueTask.CompletedTask;
    }

    private static string TrimForException(string rawBody)
    {
        return rawBody.Length <= 512 ? rawBody : "…" + rawBody[^512..];
    }
}
