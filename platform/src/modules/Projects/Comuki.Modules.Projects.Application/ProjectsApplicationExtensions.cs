using Comuki.Modules.Projects.Application.Admission;
using Comuki.Modules.Projects.Application.DomainTypes;
using Comuki.Modules.Projects.Application.Projects.Archive;
using Comuki.Modules.Projects.Application.Projects.Create;
using Comuki.Modules.Projects.Application.Projects.Queries;
using Comuki.Modules.Projects.Application.Projects.Update;
using Comuki.Modules.Projects.Application.Settings;
using Comuki.Modules.Projects.Application.Settings.Cache;
using Comuki.Modules.Projects.Application.Settings.DistributedCache;
using Comuki.Modules.Projects.Application.Settings.Update;
using FluentValidation;
using Microsoft.Extensions.Caching.Distributed;
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
    /// <summary>
    /// Registers the Projects application services. The snapshot cache
    /// resolves to <see cref="DistributedProjectSettingsCache"/> when
    /// <see cref="IDistributedCache"/> is registered (multi-replica
    /// deployments via <c>Comuki.Shared.Redis.RedisCacheExtensions.AddComukiRedisCache</c>);
    /// otherwise it falls back to the in-process <see cref="ProjectSettingsCache"/>
    /// — same interface, single-replica default (<c>Redis:Enabled</c>
    /// unset). <see cref="DistributedProjectSettingsCache"/> is
    /// deliberately registered via a <em>factory</em>, not
    /// <c>AddSingleton&lt;DistributedProjectSettingsCache&gt;()</c>: ASP.NET
    /// Core's <c>ValidateOnBuild</c> (on by default in Development) eagerly
    /// resolves the constructor call site of every type-based
    /// registration, so a type-based registration here would fail
    /// <c>Build()</c> whenever <see cref="IDistributedCache"/> is absent —
    /// exactly the single-replica default. A factory delegate is opaque to
    /// that eager check; the delegate only runs when something actually
    /// requests <see cref="DistributedProjectSettingsCache"/>, which the
    /// snapshot-cache factory below only does once it has confirmed
    /// <see cref="IDistributedCache"/> is present.
    /// </summary>
    /// <param name="services"></param>
    /// <returns></returns>
    public static IServiceCollection AddProjectsApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddMemoryCache();

        services.AddSingleton<ProjectSettingsCache>();
        services.AddSingleton(static serviceProvider =>
            new DistributedProjectSettingsCache(serviceProvider.GetRequiredService<IDistributedCache>()));
        services.AddSingleton<IProjectSettingsSnapshotCache>(static serviceProvider =>
            serviceProvider.GetService<IDistributedCache>() is not null
                ? serviceProvider.GetRequiredService<DistributedProjectSettingsCache>()
                : serviceProvider.GetRequiredService<ProjectSettingsCache>());
        services.AddSingleton<IProjectDomainTypeResolver, ProjectDomainTypeResolver>();

        services.AddScoped<CreateProjectHandler>();
        services.AddScoped<UpdateProjectHandler>();
        services.AddScoped<ArchiveProjectHandler>();
        services.AddScoped<GetProjectHandler>();
        services.AddScoped<ListProjectsHandler>();
        services.AddScoped<UpdateSettingsHandler>();
        services.AddScoped<GetProjectSettingsHandler>();

        // Scoped: it reads the scoped admission store (one EF context per
        // unit of work) on top of the singleton settings store + resolver.
        services.AddScoped<DomainTypeAdmissionService>();

        services.AddScoped<IValidator<CreateProjectCommand>, CreateProjectValidator>();
        services.AddScoped<IValidator<UpdateProjectCommand>, UpdateProjectValidator>();
        services.AddScoped<IValidator<UpdateSettingsCommand>, UpdateSettingsValidator>();

        return services;
    }
}
