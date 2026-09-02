using Comuki.Modules.Intake.Application.Ports.Sources;
using Comuki.Modules.Intake.Application.Ports.Sync;
using Comuki.Modules.Intake.Infrastructure.Providers;
using Comuki.Modules.Intake.Infrastructure.Providers.GitHub;
using Comuki.Modules.Intake.Infrastructure.Providers.GitLab;
using Comuki.Modules.Intake.Infrastructure.Providers.Jira;
using Comuki.Modules.Intake.Infrastructure.Providers.YandexTracker;
using Microsoft.Extensions.DependencyInjection;

namespace Comuki.Modules.Intake.Infrastructure;

/// <summary>
/// One registration extension for the tracker providers: four named
/// HTTP clients (each with the standard resilience handler) plus the
/// per-tracker <see cref="ITicketSourceProvider"/> /
/// <see cref="ITicketSyncPort"/> implementations. Plain AddSingleton
/// registrations — the registry resolves by source key, and a test that
/// pre-registers a fake for the same key wins (DI resolves the
/// IEnumerable in registration order, first match wins).
/// </summary>
public static class IntakeProvidersExtensions
{
    /// <summary>Registers the tracker HTTP clients and provider implementations.</summary>
    /// <param name="services"></param>
    /// <returns></returns>
    public static IServiceCollection AddIntakeProviders(this IServiceCollection services)
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

        services.AddSingleton<ITicketSyncPort, GitHubTicketSync>();
        services.AddSingleton<ITicketSyncPort, GitLabTicketSync>();
        services.AddSingleton<ITicketSyncPort, YandexTrackerTicketSync>();
        services.AddSingleton<ITicketSyncPort, JiraTicketSync>();

        return services;
    }
}
