using Comuki.TestFakeModel.Cassettes.Hosting;
using Comuki.TestFakeModel.Cassettes.IO;
using Comuki.TestFakeModel.Cassettes.Matching;
using Comuki.TestFakeModel.Cassettes.Redaction;
using Comuki.TestFakeModel.Cassettes.Wire;

namespace Comuki.TestFakeModel.Cassettes.Recording;

/// <summary>
/// Orchestrates one <c>record</c>-mode exchange: forward to upstream
/// (<see cref="CassetteUpstreamForwarder"/>), redact the captured response
/// (<see cref="CassetteRedactor"/> — before any byte reaches
/// <see cref="CassetteWriter"/>, per design.md), then append it. Every
/// call serializes on <see cref="gate"/>: <see cref="CassetteWriter.AppendExchangeAsync"/>
/// is read-modify-write against the same file, so two exchanges recorded
/// concurrently would otherwise race and one could silently disappear.
/// </summary>
/// <remarks>Creates the state for one recording session.</remarks>
internal sealed class CassetteRecordingState(
    string cassettePath,
    string recordedAgainst,
    string scenario,
    TimeProvider clock,
    CassetteUpstreamForwarder forwarder,
    BudgetTracker? tracker = null,
    decimal usdPerMillionInputTokens = 3m,
    decimal usdPerMillionOutputTokens = 15m) : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>The optional pre-forward budget tracker the recording endpoint checks before calling upstream. <c>null</c> = no enforcement.</summary>
    public BudgetTracker? Tracker => tracker;

    /// <summary>Forwards, redacts, and appends one exchange; returns the redacted response actually served to the caller.</summary>
    public async Task<CassetteResponse> RecordAsync(
        HttpContext context,
        string method,
        string path,
        string rawBody,
        ObservedRequest observed,
        CancellationToken cancellationToken)
    {
        var captured = await forwarder.ForwardAsync(context, rawBody, cancellationToken);

        if (tracker is not null)
        {
            var usage = AnthropicUsageExtractor.Extract(captured);
            var spendMicros = ComputeUsdMicros(usage, usdPerMillionInputTokens, usdPerMillionOutputTokens);
            tracker.Add(spendMicros, usage.InputTokens, usage.OutputTokens);
        }

        var redacted = captured with
        {
            Body = captured.Body is { } body ? CassetteRedactor.Redact(body) : null,
            Events = captured.Events?.Select(static sseEvent => sseEvent with { Data = CassetteRedactor.Redact(sseEvent.Data) }).ToArray(),
        };

        var exchange = new CassetteExchange(new CassetteRequest(method, path, CassetteMatchKeyBuilder.Build(observed)), redacted);

        await gate.WaitAsync(cancellationToken);
        try
        {
            await CassetteWriter.AppendExchangeAsync(cassettePath, scenario, recordedAgainst, clock.GetUtcNow(), exchange, cancellationToken);
        }
        finally
        {
            gate.Release();
        }

        return redacted;
    }

    /// <summary>
    /// Computes the micro-USD cost of one usage observation under the
    /// configured pricing defaults. Same formula as
    /// <c>Comuki.Modules.Proxy.Application.Metering.ProxyPricingCalculator.ComputeUsdMicros</c>
    /// — duplicated here to keep this project dependency-free.
    /// </summary>
    private static long ComputeUsdMicros(AnthropicUsageExtractor.UsageCounts usage, decimal usdPerMillionIn, decimal usdPerMillionOut)
    {
        var inputUsd = usage.InputTokens / 1_000_000m * usdPerMillionIn;
        var outputUsd = usage.OutputTokens / 1_000_000m * usdPerMillionOut;
        var total = inputUsd + outputUsd;
        var micros = total * 1_000_000m;
        return (long)decimal.Round(micros, MidpointRounding.AwayFromZero);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        gate.Dispose();
    }
}
