using Comuki.Modules.Proxy.Application.Models;

namespace Comuki.Modules.Proxy.Application.Ports;

/// <summary>
/// Parses a metered proxy response body to produce a
/// <see cref="ProxyUsageReport"/>. Different providers ship different shapes
/// (OpenAI: <c>usage.prompt_tokens</c> / <c>usage.completion_tokens</c>;
/// Anthropic: <c>usage.input_tokens</c> / <c>usage.output_tokens</c>) —
/// the extractor family handles one provider each. Implementations are
/// stateless; the meter invokes them once per forwarded response.
/// </summary>
public interface IProxyUsageExtractor
{
    /// <summary>Stable provider id (<c>openai</c> / <c>anthropic</c>) this extractor matches.</summary>
    public string ProviderId { get; }

    /// <summary>
    /// Parses the raw response body and returns a populated usage report
    /// or <c>null</c> when the body has no metered usage (e.g. streaming
    /// chunks, error envelopes). The caller is expected to log and skip
    /// silently on null — no usage data on one call is not an error.
    /// </summary>
    /// <param name="body">Raw JSON body the upstream returned.</param>
    /// <param name="projectId">Project attribution for the report.</param>
    /// <param name="occurredAt">Wall-clock the upstream finished the call.</param>
    /// <param name="cancellationToken"></param>
    public Task<ProxyUsageReport?> ExtractAsync(
        string body,
        Shared.Kernel.Ids.ProjectId projectId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default);
}
