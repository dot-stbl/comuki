using Comuki.Modules.Proxy.Application.Metering;
using Comuki.Modules.Proxy.Application.Options;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Proxy.Unit;

/// <summary>Pricing calculator: USD micros per call from input/output tokens + per-model price tier.</summary>
public sealed class ProxyPricingCalculatorShould
{
    [Fact(DisplayName = "Given a model with override pricing, when ComputeUsdMicros runs, then the override is applied")]
    public void OverridePricingUsedForKnownModel()
    {
        var calculator = new ProxyPricingCalculator(Options.Create(new ProxyOptions
        {
            DefaultPricing = new ProxyOptions.PricingTier(InputUsdPerMillion: 3m, OutputUsdPerMillion: 15m),
            Pricing = new Dictionary<string, ProxyOptions.PricingTier>(StringComparer.OrdinalIgnoreCase)
            {
                ["gpt-4o"] = new ProxyOptions.PricingTier(InputUsdPerMillion: 5m, OutputUsdPerMillion: 15m),
            },
        }));

        var micros = calculator.ComputeUsdMicros("gpt-4o", inputTokens: 1_000_000, outputTokens: 0);

        micros.ShouldBe(5_000_000);
    }

    [Fact(DisplayName = "Given an unknown model, when ComputeUsdMicros runs, then the default tier is applied")]
    public void DefaultPricingUsedForUnknownModel()
    {
        var calculator = new ProxyPricingCalculator(Options.Create(new ProxyOptions
        {
            DefaultPricing = new ProxyOptions.PricingTier(InputUsdPerMillion: 3m, OutputUsdPerMillion: 15m),
        }));

        var micros = calculator.ComputeUsdMicros("gpt-future", inputTokens: 1_000_000, outputTokens: 1_000_000);

        micros.ShouldBe(18_000_000);
    }

    [Fact(DisplayName = "Given zero tokens, when ComputeUsdMicros runs, then zero micros are returned returned")]
    public void ZeroTokensReturnsZero()
    {
        var calculator = new ProxyPricingCalculator(Options.Create(new ProxyOptions()));

        var micros = calculator.ComputeUsdMicros("any", inputTokens: 0, outputTokens: 0);

        micros.ShouldBe(0);
    }
}
