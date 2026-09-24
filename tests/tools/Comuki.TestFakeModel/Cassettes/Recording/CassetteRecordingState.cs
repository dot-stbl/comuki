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
    CassetteUpstreamForwarder forwarder) : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);

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

    /// <inheritdoc />
    public void Dispose()
    {
        gate.Dispose();
    }
}
