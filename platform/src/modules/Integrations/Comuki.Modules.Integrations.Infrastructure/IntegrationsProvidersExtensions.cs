using Comuki.Modules.Integrations.Application.Ports.Sources;
using Comuki.Modules.Integrations.Application.Ports.Sync;
using Comuki.Modules.Integrations.Infrastructure.Providers;
using Comuki.Modules.Integrations.Infrastructure.Providers.GitHub;
using Comuki.Modules.Integrations.Infrastructure.Providers.GitLab;
using Comuki.Modules.Integrations.Infrastructure.Providers.Jira;
using Comuki.Modules.Integrations.Infrastructure.Providers.YandexTracker;
using Microsoft.Extensions.DependencyInjection;

namespace Comuki.Modules.Integrations.Infrastructure;

/// <summary>
/// One registration extension for the tracker providers: four named
/// HTTP clients (each with the standard resilience handler) plus the
/// per-tracker <see cref="ITicketSourceProvider"/> /
/// <see cref="IIntegrationSyncPort"/> implementations. Plain AddSingleton
/// registrations — the registry resolves by source key, and a test that
/// pre-registers a fake for the same key wins (DI resolves the
/// IEnumerable in registration order, first match wins).
/// </summary>
public static class IntegrationsProvidersExtensions
{
    /// <summary>Registers the tracker HTTP clients and provider implementations.</summary>
    /// <param name="services"></param>
    /// <returns></returns>
    public static IServiceCollection AddIntegrationsProviders(this IServiceCollection services)
    {
        services.AddHttpClient(TrackerHttp.GitHubClient)
            .AddStandardResilienceHandler();
        services.AddHttpClient(TrackerHttp.GitLabClient)
            .AddStandardResilienceHandler();
        services.AddHttpClient(TrackerHttp.YandexTrackerClient)
            .AddStandardResilienceHandler();
        services.AddHttpClient(TrackerHttp.JiraClient)
            .AddStandardResilienceHandler();

        services.AddSingleton<TrackerClientFactory>();

        services.AddSingleton<ITicketSourceProvider, GitHubTicketSourceProvider>();
        services.AddSingleton<ITicketSourceProvider, GitLabTicketSourceProvider>();
        services.AddSingleton<ITicketSourceProvider, YandexTrackerTicketSourceProvider>();
        services.AddSingleton<ITicketSourceProvider, JiraTicketSourceProvider>();

        services.AddSingleton<IIntegrationSyncPort, GitHubTicketSync>();
        services.AddSingleton<IIntegrationSyncPort, GitLabTicketSync>();
        services.AddSingleton<IIntegrationSyncPort, YandexTrackerTicketSync>();
        services.AddSingleton<IIntegrationSyncPort, JiraTicketSync>();

        return services;
    }
}
