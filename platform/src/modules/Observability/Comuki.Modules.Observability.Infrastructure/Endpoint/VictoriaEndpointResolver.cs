using Comuki.Modules.Observability.Application.Options;
using Comuki.Modules.Observability.Application.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comuki.Modules.Observability.Infrastructure.Endpoint;

/// <summary>
/// Default <see cref="IVictoriaEndpointResolver"/> implementation.
/// The resolver composes the typed <see cref="ObservabilityOptions"/>
/// (the base URLs are operator-overridable) with the deploy stack's
/// service-name defaults (<see cref="VictoriaServices"/>) and the
/// reserved container ports (<see cref="VictoriaPorts"/>). A
/// dedicated <see cref="HttpClient"/> is used for the
/// <see cref="ProbeAsync"/> health probe — the typed query clients
/// have their own HttpClient factories (per
/// <c>http-resilience-refit.md</c>).
/// </summary>
internal sealed class VictoriaEndpointResolver(
    IOptions<ObservabilityOptions> options,
    IHttpClientFactory httpClientFactory,
    ILogger<VictoriaEndpointResolver> logger) : IVictoriaEndpointResolver
{
    /// <summary>Named HttpClient the resolver uses for the <c>/health</c> probe.</summary>
    public const string HealthProbeHttpClient = "comuki.observability.victoria.probe";

    /// <inheritdoc />
    public Uri ResolveLogsUrl(CancellationToken cancellationToken = default)
    {
        return ResolveBaseUrl(
            overrideUrl: options.Value.LogsBaseUrl,
            fallbackService: VictoriaServices.LogsServiceName,
            fallbackPort: VictoriaPorts.LogsContainerPort,
            sectionName: nameof(ObservabilityOptions.LogsBaseUrl));
    }

    /// <inheritdoc />
    public Uri ResolveMetricsUrl(CancellationToken cancellationToken = default)
    {
        return ResolveBaseUrl(
            overrideUrl: options.Value.MetricsBaseUrl,
            fallbackService: VictoriaServices.MetricsServiceName,
            fallbackPort: VictoriaPorts.MetricsContainerPort,
            sectionName: nameof(ObservabilityOptions.MetricsBaseUrl));
    }

    /// <inheritdoc />
    public async Task<string> ProbeAsync(CancellationToken cancellationToken = default)
    {
        var client = httpClientFactory.CreateClient(HealthProbeHttpClient);
        var metricsUri = ResolveMetricsUrl(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(metricsUri, "/health"));
        try
        {
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? "victoria:ok"
                : $"victoria:http_{(int)response.StatusCode}";
        }
        catch (HttpRequestException exception)
        {
            logger.LogDebug(exception, "victoria health probe failed");
            return "victoria:unreachable";
        }
    }

    /// <summary>
    /// Build the base URL: operator override wins; otherwise the
    /// compose service name + container port. The returned URI carries
    /// scheme + host + port only — the Refit client owns path
    /// segments. <paramref name="sectionName"/> logs the source of the
    /// resolved URL so a deployment with a misconfigured override is
    /// debuggable from the boot log alone.
    /// </summary>
    private Uri ResolveBaseUrl(Uri? overrideUrl, string fallbackService, int fallbackPort, string sectionName)
    {
        if (overrideUrl is not null)
        {
            logger.LogDebug(
                "Victoria endpoint resolved from config ({Section}): {Url}",
                sectionName,
                overrideUrl);
            return overrideUrl;
        }

        var defaultUri = new Uri($"http://{fallbackService}:{fallbackPort}");
        logger.LogDebug(
            "Victoria endpoint defaulted to compose service name ({Section}): {Url}",
            sectionName,
            defaultUri);
        return defaultUri;
    }
}
