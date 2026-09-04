using System.Diagnostics;
using Comuki.Modules.Intake.Application.Sync;
using Comuki.Modules.Intake.Domain.Connections;
using Comuki.Modules.Intake.Domain.Tickets;

namespace Comuki.Modules.Intake.Application.Sources;

/// <summary>
/// Probes the upstream tracker with the supplied draft (issues #41-#42):
/// resolves the credential from <c>secretEnvRef</c>, opens a tracker
/// client with the per-provider settings, and asks the provider's
/// ping-equivalent endpoint (the catalog fetch — a single page). The
/// result is a small projection the dashboard renders — reachable,
/// latency, and a short provider-side status sentence. A failed probe is
/// still a 200 — the dashboard renders the provider's sentence rather
/// than a generic error.
/// </summary>
/// <param name="providers"></param>
public sealed class SourceProbeService(TicketProviderRegistry providers)
{
    /// <summary>The default timeout for the probe HTTP request.</summary>
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Probes a draft or stored connection (issues #41 and #42).</summary>
    /// <param name="provider">Kebab-case provider key.</param>
    /// <param name="settingsJson">Provider-specific, non-secret settings.</param>
    /// <param name="secretEnvRef">Env-var name holding the webhook / outbound token.</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<SourceProbeResult> ProbeDraftAsync(
        string provider,
        string settingsJson,
        string secretEnvRef,
        CancellationToken cancellationToken = default)
    {
        if (TicketProviderKeys.TryParse(provider) is not { } parsed
            || parsed is TicketProvider.Native)
        {
            return new SourceProbeResult(
                Reachable: false,
                LatencyMs: 0,
                SuggestedId: null,
                Message: $"provider '{provider}' is not probeable");
        }

        var sourceProvider = providers.FindSource(provider);

        if (sourceProvider is null)
        {
            return new SourceProbeResult(
                Reachable: false,
                LatencyMs: 0,
                SuggestedId: null,
                Message: $"provider '{provider}' is not registered");
        }

        // The probe uses a synthetic connection — the secret/env-resolve and
        // settings are caller-provided so the operator can test a draft
        // before saving. The id / webhookKey fields are unused on this path.
        var stub = SourceConnection.Create(
            default,
            parsed,
            "_probe_",
            settingsJson,
            secretEnvRef,
            "_probe_",
            DateTimeOffset.UtcNow);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ProbeTimeout);

            var page = await sourceProvider.FetchCatalogAsync(stub, page: 1, timeout.Token);

            stopwatch.Stop();

            return new SourceProbeResult(
                Reachable: true,
                LatencyMs: stopwatch.ElapsedMilliseconds,
                SuggestedId: page.Count > 0 ? null : "0-issues",
                Message: $"{provider}: reachable, {page.Count} item(s) returned");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();

            return new SourceProbeResult(
                Reachable: false,
                LatencyMs: stopwatch.ElapsedMilliseconds,
                SuggestedId: null,
                Message: $"{provider}: probe timed out after {ProbeTimeout.TotalSeconds:F0}s");
        }
        catch (Exception exception)
        {
            stopwatch.Stop();

            return new SourceProbeResult(
                Reachable: false,
                LatencyMs: stopwatch.ElapsedMilliseconds,
                SuggestedId: null,
                Message: $"{provider}: {exception.GetType().Name} — {exception.Message}");
        }
    }
}
