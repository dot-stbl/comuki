using Comuki.Modules.Projects.Application.Projects.Archive;
using Comuki.Modules.Projects.Application.Projects.Create;
using Comuki.Modules.Projects.Application.Projects.Queries;
using Comuki.Modules.Projects.Application.Projects.Update;
using Comuki.Modules.Projects.Application.Settings;
using Comuki.Modules.Projects.Application.Settings.Update;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Comuki.Modules.Projects.Application;

/// <summary>
/// Composition of the Projects application layer: command/query handlers,
/// their validators and the shared settings snapshot cache. Persistence
/// ports are satisfied by the infrastructure installer; nothing here
/// touches EF.
/// </summary>
public static class ProjectsApplicationExtensions
{
    /// <summary>Registers the Projects application services.</summary>
    /// <param name="services"></param>
    /// <returns></returns>
    public static IServiceCollection AddProjectsApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddMemoryCache();

        services.AddSingleton<ProjectSettingsCache>();

        services.AddScoped<CreateProjectHandler>();
        services.AddScoped<UpdateProjectHandler>();
        services.AddScoped<ArchiveProjectHandler>();
        services.AddScoped<GetProjectHandler>();
        services.AddScoped<ListProjectsHandler>();
        services.AddScoped<UpdateSettingsHandler>();
        services.AddScoped<GetProjectSettingsHandler>();

        services.AddScoped<IValidator<CreateProjectCommand>, CreateProjectValidator>();
        services.AddScoped<IValidator<UpdateProjectCommand>, UpdateProjectValidator>();
        services.AddScoped<IValidator<UpdateSettingsCommand>, UpdateSettingsValidator>();

        return services;
    }
}
