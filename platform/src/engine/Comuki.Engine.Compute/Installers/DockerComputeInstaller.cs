using Comuki.Engine.Compute.Options;
using Comuki.Engine.Compute.Pool;
using Comuki.Engine.Compute.Ports;
using Comuki.Engine.Compute.Providers;
using Comuki.Engine.Compute.Security;
using Comuki.Engine.Compute.Security.Stores;
using Comuki.Engine.Compute.Settings;
using Comuki.Engine.Compute.Supervisor;
using Comuki.Shared.Contracts.Compute;
using Docker.DotNet;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Comuki.Engine.Compute.Installers;

/// <summary>
/// Registers the Docker compute provider, the worker token issuer and the
/// scale supervisor (T2.4/T2.5). Wired only in a host composition root —
/// nothing else references these concretes. The host must ALSO register an
/// <see cref="IBacklogReader"/> (the Orchestration queue adapter lands with
/// the queue slice); without it the supervisor resolution fails fast.
/// </summary>
public static class DockerComputeInstaller
{
    /// <summary>Adds the compute engine: options, IDockerClient, token issuer, docker provider, scale supervisor.</summary>
    /// <param name="services"></param>
    /// <param name="configuration"></param>
    public static IServiceCollection AddComukiCompute(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DockerComputeOptions>()
            .Bind(configuration.GetSection(DockerComputeOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<WorkerTokenOptions>()
            .Bind(configuration.GetSection(WorkerTokenOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ScaleSupervisorOptions>()
            .Bind(configuration.GetSection(ScaleSupervisorOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<IWorkerTokenStore, InMemoryWorkerTokenStore>();
        services.AddSingleton<WorkerTokenIssuer>();
        services.AddSingleton<IDockerClient>(static _ => new DockerClientConfiguration().CreateClient());
        services.AddSingleton<IComputeProvider, DockerComputeProvider>();
        services.AddSingleton<IProjectScaleSettings, InMemoryProjectScaleSettings>();
        services.AddSingleton<WorkerPoolState>();
        services.AddSingleton<IWorkerPoolState>(static serviceProvider => serviceProvider.GetRequiredService<WorkerPoolState>());
        services.AddSingleton<ScaleSupervisorCycle>();
        services.AddHostedService<ScaleSupervisorWorker>();

        return services;
    }
}
