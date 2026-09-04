using Comuki.Modules.Costs.Domain.Events;
using Comuki.Modules.Proxy.Application.Extraction;
using Comuki.Modules.Proxy.Application.Metering;
using Comuki.Modules.Proxy.Application.Models;
using Comuki.Modules.Proxy.Application.Options;
using Comuki.Modules.Proxy.Application.Ports;
using Comuki.Shared.Contracts.Costs;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Comuki.Modules.Proxy.Unit;

/// <summary>Meter end-to-end: extractor picks the report, pricing fills in the cost, recorder sees the call.</summary>
public sealed class ProxyUsageMeterShould
{
    [Fact(DisplayName = "Given a 2xx body with OpenAI usage, when MeterAsync runs, then the recorder receives a proxy-source record")]
    public async Task RecordsOpenAiUsageAsync()
    {
        var projectId = ProjectId.New();
        var recorder = Substitute.For<IUsageRecorder>();
        var extractor = new OpenAiUsageExtractor();
        var pricing = new ProxyPricingCalculator(new ProxyOptions
        {
            DefaultPricing = new ProxyOptions.PricingTier(InputUsdPerMillion: 3m, OutputUsdPerMillion: 15m),
        });
        var meter = new ProxyUsageMeter([extractor], pricing, recorder, NullLogger<ProxyUsageMeter>.Instance);

        var body = /*lang=json,strict*/ """
        { "model": "gpt-4o-mini", "usage": { "prompt_tokens": 1000, "completion_tokens": 500 } }
        """;
        var key = new VirtualKey("vkey", projectId, new UpstreamSpec("openai", "https://api.openai.com", "OPENAI_API_KEY"));

        await meter.MeterAsync("openai", body, key, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        await recorder.Received(1).RecordAsync(
            Arg.Is<UsageRecord>(record =>
                record.ProjectId == projectId
                && record.Source == UsageSourceKeys.Proxy
                && record.Model == "gpt-4o-mini"
                && record.InputTokens == 1000
                && record.OutputTokens == 500
                && record.CostUsdMicros == 3_000 + 7_500),
            Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a body without usage, when MeterAsync runs, then the recorder is not invoked")]
    public async Task SkipsRecordingWhenUsageMissingAsync()
    {
        var recorder = Substitute.For<IUsageRecorder>();
        var extractor = Substitute.For<IProxyUsageExtractor>();
        _ = extractor.ProviderId.Returns("openai");
        _ = extractor.ExtractAsync(Arg.Any<string>(), Arg.Any<ProjectId>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ProxyUsageReport?>(null));
        var pricing = new ProxyPricingCalculator(new ProxyOptions());
        var meter = new ProxyUsageMeter([extractor], pricing, recorder, NullLogger<ProxyUsageMeter>.Instance);
        var key = new VirtualKey("vkey", ProjectId.New(), new UpstreamSpec("openai", "https://api.openai.com", "OPENAI_API_KEY"));

        await meter.MeterAsync("openai", body: "{ }", key, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        await recorder.DidNotReceiveWithAnyArgs().RecordAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "Given a body for a provider with no extractor, when MeterAsync runs, then the recorder is not invoked")]
    public async Task SkipsRecordingWhenProviderHasNoExtractorAsync()
    {
        var recorder = Substitute.For<IUsageRecorder>();
        var extractor = Substitute.For<IProxyUsageExtractor>();
        _ = extractor.ProviderId.Returns("openai");
        var pricing = new ProxyPricingCalculator(new ProxyOptions());
        var meter = new ProxyUsageMeter([extractor], pricing, recorder, NullLogger<ProxyUsageMeter>.Instance);
        var key = new VirtualKey("vkey", ProjectId.New(), new UpstreamSpec("anthropic", "https://api.anthropic.com", "ANTHROPIC_API_KEY"));

        await meter.MeterAsync("anthropic", body: "{ }", key, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        await recorder.DidNotReceiveWithAnyArgs().RecordAsync(default!, TestContext.Current.CancellationToken);
    }
}
