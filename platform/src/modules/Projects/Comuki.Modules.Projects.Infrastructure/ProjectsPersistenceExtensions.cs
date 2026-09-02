using Comuki.Modules.Projects.Application.Ports;
using Comuki.Modules.Projects.Infrastructure.Persistence;
using Comuki.Modules.Projects.Infrastructure.Persistence.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace Comuki.Modules.Projects.Infrastructure;

/// <summary>Registration entry point for Projects persistence.</summary>
public static class ProjectsPersistenceExtensions
{
    /// <summary>
    /// Registers <see cref="ProjectsDbContext"/> (Npgsql + snake_case +
    /// private migrations history via <see cref="ProjectsDbContext.ApplyOptions"/>),
    /// the project store (scoped — one context per unit of work) and the
    /// singleton settings store with its cache refresher. The factory
    /// registration also provides a scoped <see cref="ProjectsDbContext"/>
    /// for request-scoped consumers; the settings store and refresher use
    /// the singleton factory directly (they outlive any scope).
    /// </summary>
    /// <param name="services"></param>
    /// <param name="connectionString"></param>
    /// <returns></returns>
    public static IServiceCollection AddProjectsPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContextFactory<ProjectsDbContext>(options =>
            ProjectsDbContext.ApplyOptions(options, connectionString));

        services.AddScoped<IProjectStore, ProjectStore>();
        services.AddSingleton<IProjectSettingsStore, DbProjectSettingsStore>();
        services.AddHostedService<ProjectSettingsCacheRefresher>();

        return services;
    }
}
