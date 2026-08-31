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
        _ = services.AddMemoryCache();

        _ = services.AddSingleton<ProjectSettingsCache>();

        _ = services.AddScoped<CreateProjectHandler>();
        _ = services.AddScoped<UpdateProjectHandler>();
        _ = services.AddScoped<ArchiveProjectHandler>();
        _ = services.AddScoped<GetProjectHandler>();
        _ = services.AddScoped<ListProjectsHandler>();
        _ = services.AddScoped<UpdateSettingsHandler>();
        _ = services.AddScoped<GetProjectSettingsHandler>();

        _ = services.AddScoped<IValidator<CreateProjectCommand>, CreateProjectValidator>();
        _ = services.AddScoped<IValidator<UpdateProjectCommand>, UpdateProjectValidator>();
        _ = services.AddScoped<IValidator<UpdateSettingsCommand>, UpdateSettingsValidator>();

        return services;
    }
}
