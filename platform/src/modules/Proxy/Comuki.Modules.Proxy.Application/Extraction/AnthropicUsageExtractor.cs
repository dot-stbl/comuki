using System.Text.Json;
using Comuki.Modules.Proxy.Application.Models;
using Comuki.Modules.Proxy.Application.Ports;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Proxy.Application.Extraction;

/// <summary>
/// Parses the Anthropic <c>/v1/messages</c> response shape:
/// <c>model</c> at the root, <c>usage.input_tokens</c> +
/// <c>usage.output_tokens</c> underneath. Streaming responses (SSE) are
/// pre-concatenated by the response-capture transformer before this method
/// sees the body.
/// </summary>
public sealed class AnthropicUsageExtractor : IProxyUsageExtractor
{
    private const string ProviderIdValue = "anthropic";

    /// <inheritdoc />
    public string ProviderId => ProviderIdValue;

    /// <inheritdoc />
    public Task<ProxyUsageReport?> ExtractAsync(
        string body,
        ProjectId projectId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Task.FromResult<ProxyUsageReport?>(null);
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Task.FromResult<ProxyUsageReport?>(null);
            }

            if (!root.TryGetProperty("usage", out var usageElement)
                || usageElement.ValueKind != JsonValueKind.Object)
            {
                return Task.FromResult<ProxyUsageReport?>(null);
            }

            var inputTokens = ReadInt(usageElement, "input_tokens");
            var outputTokens = ReadInt(usageElement, "output_tokens");
            var model = root.TryGetProperty("model", out var modelElement) && modelElement.ValueKind == JsonValueKind.String
                ? modelElement.GetString() ?? string.Empty
                : string.Empty;

            return inputTokens == 0 && outputTokens == 0
                ? Task.FromResult<ProxyUsageReport?>(null)
                : Task.FromResult<ProxyUsageReport?>(new ProxyUsageReport(
                    projectId,
                    string.IsNullOrWhiteSpace(model) ? "unknown" : model,
                    inputTokens,
                    outputTokens,
                    CostUsdMicros: 0,
                    OccurredAt: occurredAt));
        }
        catch (JsonException)
        {
            return Task.FromResult<ProxyUsageReport?>(null);
        }
    }

    private static int ReadInt(JsonElement parent, string propertyName)
    {
        return !parent.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Number
            ? 0
            : element.TryGetInt32(out var value) ? value : 0;
    }
}
