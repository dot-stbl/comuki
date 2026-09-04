using Comuki.Modules.Proxy.Application.Options;

namespace Comuki.Modules.Proxy.Application.Metering;

/// <summary>
/// Computes USD-micros from input / output tokens + a per-model price tier.
/// Lookup order: explicit <see cref="ProxyOptions.Pricing"/> entry (case-
/// insensitive), then <see cref="ProxyOptions.DefaultPricing"/>.
/// </summary>
/// <param name="options">Pricing configuration.</param>
public sealed class ProxyPricingCalculator(ProxyOptions options)
{
    /// <summary>USD-micros for one call.</summary>
    /// <param name="model">Model id (case-insensitive).</param>
    /// <param name="inputTokens">Prompt / input tokens.</param>
    /// <param name="outputTokens">Completion / output tokens.</param>
    public long ComputeUsdMicros(string model, int inputTokens, int outputTokens)
    {
        var tier = options.Pricing.TryGetValue(model, out var overrideTier)
            ? overrideTier
            : options.DefaultPricing;

        var inputUsd = inputTokens / 1_000_000m * tier.InputUsdPerMillion;
        var outputUsd = outputTokens / 1_000_000m * tier.OutputUsdPerMillion;
        var total = inputUsd + outputUsd;
        var micros = total * 1_000_000m;
        return (long)decimal.Round(micros, MidpointRounding.AwayFromZero);
    }
}
