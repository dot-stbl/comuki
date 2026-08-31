using Comuki.Engine.Compute.Options;
using Comuki.Engine.Compute.Providers;
using Comuki.Engine.Compute.Security;
using Comuki.Engine.Compute.Security.Stores;
using Comuki.Shared.Contracts.Compute;
using Docker.DotNet;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Comuki.Engine.Compute.Installers;

/// <summary>
/// Registers the Docker compute provider and the worker token issuer. Wired
/// only in a host composition root — nothing else references these concretes.
/// </summary>
public static class DockerComputeInstaller
{
    /// <summary>Adds the compute engine: options, IDockerClient, token issuer, docker provider.</summary>
    /// <param name="services"></param>
    /// <param name="configuration"></param>
    public static IServiceCollection AddComukiCompute(this IServiceCollection services, IConfiguration configuration)
    {
        _ = services.AddOptions<DockerComputeOptions>()
            .Bind(configuration.GetSection(DockerComputeOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        _ = services.AddOptions<WorkerTokenOptions>()
            .Bind(configuration.GetSection(WorkerTokenOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);

        _ = services.AddSingleton<IWorkerTokenStore, InMemoryWorkerTokenStore>();
        _ = services.AddSingleton<WorkerTokenIssuer>();
        _ = services.AddSingleton<IDockerClient>(static _ => new DockerClientConfiguration().CreateClient());
        _ = services.AddSingleton<IComputeProvider, DockerComputeProvider>();

        return services;
    }
}
