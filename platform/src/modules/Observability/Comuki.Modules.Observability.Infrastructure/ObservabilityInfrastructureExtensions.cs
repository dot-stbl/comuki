using Comuki.Modules.Observability.Application.Ports;
using Comuki.Modules.Observability.Infrastructure.Endpoint;
using Comuki.Modules.Observability.Infrastructure.VictoriaLogs;
using Comuki.Modules.Observability.Infrastructure.VictoriaMetrics;
using Microsoft.Extensions.DependencyInjection;

namespace Comuki.Modules.Observability.Infrastructure;

/// <summary>
/// Composition entry point for the Observability module. Registers
/// the <see cref="IVictoriaEndpointResolver"/>, the health-probe
/// HttpClient, and the two typed query clients. Each typed client
/// constructs its own Refit surface from a private HttpClient bound
/// to the resolver's base URL at first resolution — this keeps the
/// wire-level URL discovery inside the resolver (the single source of
/// truth for the operator's <c>LogsBaseUrl</c> / <c>MetricsBaseUrl</c>
/// overrides) and avoids the Refit <c>ConfigureHttpClient</c>
/// overload that doesn't expose the service provider.
/// </summary>
public static class ObservabilityInfrastructureExtensions
{
    /// <summary>
    /// Registers the observability typed clients + endpoint resolver
    /// + health probe HttpClient. The host composition calls this
    /// after <c>AddObservabilityApplication</c>.
    /// </summary>
    /// <param name="services">The host service collection.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddObservabilityInfrastructure(this IServiceCollection services)
    {
        // The endpoint resolver is the seam between the typed
        // ObservabilityOptions and the wire-level HTTP clients — it
        // is the one place the resolve happens (singleton so the URL
        // is stable for the process lifetime).
        services.AddSingleton<IVictoriaEndpointResolver, VictoriaEndpointResolver>();

        // The health-probe HttpClient lives next to the resolver so
        // a single named-client registration covers the probe wiring.
        // No resilience: a single failing probe should fail the host's
        // IHealthCheck with the raw status — circuit breakers around
        // the probe would mask a real outage as a transient.
        services.AddHttpClient(VictoriaEndpointResolver.HealthProbeHttpClient);

        // The two typed query clients. Both take the endpoint resolver
        // (singleton) and construct a private HttpClient + Refit proxy
        // at construction. The private HttpClient carries the
        // resilience handler so the upstream's transient-failure
        // policy applies uniformly to both the typed clients and the
        // health probe.
        services.AddSingleton<IVictoriaLogsQueryClient, VictoriaLogsQueryClient>();
        services.AddSingleton<IVictoriaMetricsQueryClient, VictoriaMetricsQueryClient>();

        return services;
    }
}
