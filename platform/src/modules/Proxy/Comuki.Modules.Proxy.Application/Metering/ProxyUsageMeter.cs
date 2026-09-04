using System.Text.Json;
using Comuki.Modules.Costs.Domain.Events;
using Comuki.Modules.Proxy.Application.Models;
using Comuki.Modules.Proxy.Application.Ports;
using Comuki.Shared.Contracts.Costs;
using Microsoft.Extensions.Logging;

namespace Comuki.Modules.Proxy.Application.Metering;

/// <summary>
/// Picks the matching extractor for the response's provider, computes the
/// cost, and forwards the report to <see cref="IUsageRecorder"/> (costs
/// module). Failures to meter are logged at warning level — a recording
/// miss must never fail the request the caller has already paid for.
/// </summary>
/// <param name="extractors">Every registered extractor (keyed by provider id).</param>
/// <param name="pricing">Pricing calculator.</param>
/// <param name="recorder">Costs module usage recorder.</param>
/// <param name="logger">Structured logger.</param>
public sealed class ProxyUsageMeter(
    IEnumerable<IProxyUsageExtractor> extractors,
    ProxyPricingCalculator pricing,
    IUsageRecorder recorder,
    ILogger<ProxyUsageMeter> logger)
{
    /// <summary>Meters one response body. Never throws; failures are logged.</summary>
    /// <param name="provider">Provider cluster id (<c>openai</c> / <c>anthropic</c>).</param>
    /// <param name="body">Raw JSON body the upstream returned.</param>
    /// <param name="key">Key the caller presented (for project attribution).</param>
    /// <param name="occurredAt">Wall-clock the call finished.</param>
    /// <param name="cancellationToken"></param>
    public async Task MeterAsync(
        string provider,
        string body,
        VirtualKey key,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default)
    {
        var extractor = extractors.FirstOrDefault(candidate => candidate.ProviderId == provider);
        if (extractor is null)
        {
            logger.LogDebug("No usage extractor registered for provider {Provider}", provider);
            return;
        }

        ProxyUsageReport? report;
        try
        {
            report = await extractor.ExtractAsync(body, key.ProjectId, occurredAt, cancellationToken);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Usage extractor for provider {Provider} failed to parse response body", provider);
            return;
        }

        if (report is null)
        {
            return;
        }

        var costMicros = pricing.ComputeUsdMicros(report.Model, report.InputTokens, report.OutputTokens);
        var enriched = report with { CostUsdMicros = costMicros };

        try
        {
            await recorder.RecordAsync(
                new UsageRecord(
                    ProjectId: enriched.ProjectId,
                    RunId: null,
                    Source: UsageSourceKeys.Proxy,
                    Model: enriched.Model,
                    InputTokens: enriched.InputTokens,
                    OutputTokens: enriched.OutputTokens,
                    CostUsdMicros: enriched.CostUsdMicros,
                    OccurredAt: enriched.OccurredAt),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to record usage for project {ProjectId}", enriched.ProjectId);
        }
    }
}
