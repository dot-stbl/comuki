using Microsoft.Extensions.Options;
using Refit;

namespace Comuki.Host.Translator.Api;

/// <summary>
/// Registers the Refit client for <see cref="IOrchestratorApi"/>: base
/// address from <see cref="TranslatorOptions"/>, bearer token handler and
/// the standard resilience pipeline (retry + circuit breaker + timeout).
/// </summary>
public static class TranslatorApiExtensions
{
    /// <summary>Adds the orchestrator Refit client with auth + resilience.</summary>
    /// <param name="services"></param>
    public static IServiceCollection AddOrchestratorApi(this IServiceCollection services)
    {
        _ = services.AddTransient<WorkerTokenHandler>();
        _ = services
            .AddRefitClient<IOrchestratorApi>()
            .ConfigureHttpClient(static (serviceProvider, client) =>
                client.BaseAddress = serviceProvider.GetRequiredService<IOptions<TranslatorOptions>>().Value.OrchestratorBaseUrl)
            .AddHttpMessageHandler<WorkerTokenHandler>()
            .AddStandardResilienceHandler();

        return services;
    }
}
