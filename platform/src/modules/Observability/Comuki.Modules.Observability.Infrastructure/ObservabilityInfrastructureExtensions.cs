using Comuki.Modules.Observability.Application.Options;
using Comuki.Modules.Observability.Application.Ports;
using Comuki.Modules.Observability.Infrastructure.VictoriaLogs;
using Comuki.Modules.Observability.Infrastructure.VictoriaMetrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Refit;

namespace Comuki.Modules.Observability.Infrastructure;

/// <summary>
/// Composition entry point for the Observability module. Registers the
/// two typed query clients through the standard
/// <c>AddRefitClient&lt;T&gt;().ConfigureHttpClient(...).AddStandardResilienceHandler()</c>
/// pipeline (retry + circuit breaker + timeout), reading the base URLs
/// from <see cref="ObservabilityOptions"/> which carries the
/// compose-service-name defaults in its property initializers. The
/// pattern mirrors the documented
/// <c>Comuki.Host.Translator.Api.Registration.TranslatorApiExtensions.AddOrchestratorApi</c>
/// — one Refit registration per surface, single setup surface, no
/// hand-rolled <c>new HttpClient</c> in the typed clients. The
/// bind-override path (operator-supplied <c>LogsBaseUrl</c> /
/// <c>MetricsBaseUrl</c>) flows through
/// <see cref="IOptions{TOptions}"/>; <see cref="ObservabilityOptions"/>
/// property initializers carry the deploy-stack default so an absent
/// config section still resolves at boot.
/// </summary>
public static class ObservabilityInfrastructureExtensions
{
    /// <summary>
    /// Registers the observability typed clients + the typed-options
    /// binding. The host composition calls this after
    /// <c>AddObservabilityApplication</c>.
    /// </summary>
    /// <param name="services">The host service collection.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddObservabilityInfrastructure(this IServiceCollection services)
    {
        services
            .AddRefitClient<IVictoriaLogsApi>()
            .ConfigureHttpClient(static (serviceProvider, client) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<ObservabilityOptions>>().Value;
                client.BaseAddress = ObservabilityBaseUrl.ResolveLogs(options);
            })
            .AddStandardResilienceHandler();

        services
            .AddRefitClient<IVictoriaMetricsApi>()
            .ConfigureHttpClient(static (serviceProvider, client) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<ObservabilityOptions>>().Value;
                client.BaseAddress = ObservabilityBaseUrl.ResolveMetrics(options);
            })
            .AddStandardResilienceHandler();

        services.AddSingleton<IVictoriaLogsQueryClient, VictoriaLogsQueryClient>();
        services.AddSingleton<IVictoriaMetricsQueryClient, VictoriaMetricsQueryClient>();

        return services;
    }
}

/// <summary>
/// Base-URL resolution for the two Refit clients — operator override
/// wins, otherwise the deploy stack's compose service name is the
/// default. Lives in a <c>file static class</c> per
/// <c>class-layout-and-tooling.md §1a</c> (no private helpers on the
/// installer).
/// </summary>
file static class ObservabilityBaseUrl
{
    /// <summary>Default resolve: the operator override, or the deploy stack's compose service name.</summary>
    /// <param name="options">Bound observability options.</param>
    public static Uri ResolveLogs(ObservabilityOptions options)
    {
        return options.LogsBaseUrl
            ?? new Uri($"http://{ObservabilityOptions.DefaultLogsServiceName}:{ObservabilityOptions.DefaultLogsPort}");
    }

    /// <summary>Default resolve: the operator override, or the deploy stack's compose service name.</summary>
    /// <param name="options">Bound observability options.</param>
    public static Uri ResolveMetrics(ObservabilityOptions options)
    {
        return options.MetricsBaseUrl
            ?? new Uri($"http://{ObservabilityOptions.DefaultMetricsServiceName}:{ObservabilityOptions.DefaultMetricsPort}");
    }
}
