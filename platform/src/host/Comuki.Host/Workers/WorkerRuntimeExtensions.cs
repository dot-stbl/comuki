using Comuki.Engine.Compute.Options;
using Comuki.Engine.Compute.Security;
using Comuki.Engine.Compute.Security.Stores;
using Comuki.Host.Workers.Api;
using Comuki.Host.Workers.Grpc;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProtoBuf.Grpc.Server;

namespace Comuki.Host.Workers;

/// <summary>
/// Composition of the worker runtime (T3.2/T3.3): the code-first gRPC
/// server for <see cref="WorkerGrpcService"/> plus the worker REST
/// claim/heartbeat surface. Call after orchestration persistence/queue and
/// compute security are registered (the extensions below try-add the token
/// issuer when the compute engine has not already registered it).
/// </summary>
public static class WorkerRuntimeExtensions
{
    /// <summary>Registers the token authenticator, command hub and the worker gRPC service.</summary>
    /// <param name="services"></param>
    /// <param name="configuration"></param>
    public static IServiceCollection AddWorkerRuntime(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<WorkerTokenOptions>()
            .Bind(configuration.GetSection(WorkerTokenOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IWorkerTokenStore, InMemoryWorkerTokenStore>();
        services.TryAddSingleton<WorkerTokenIssuer>();
        services.AddSingleton<WorkerTokenAuthenticator>();
        services.AddSingleton<WorkerCommandHub>();
        services.AddSingleton<IWorkerCommandPipe>(static serviceProvider =>
            serviceProvider.GetRequiredService<WorkerCommandHub>());
        services.AddScoped<WorkerGrpcService>();
        services.AddCodeFirstGrpc();

        return services;
    }

    /// <summary>Maps the worker gRPC service and the worker REST endpoints.</summary>
    /// <param name="app"></param>
    public static void MapWorkerRuntime(this WebApplication app)
    {
        app.MapWorkerGrpc();
        app.MapWorkerRest();
    }

    /// <summary>
    /// Maps only the bidi gRPC service. Hosts that register no orchestration
    /// application services (REST claim handlers) must use this overload —
    /// mapping REST without its handlers fails parameter binding per request.
    /// </summary>
    /// <param name="app"></param>
    public static void MapWorkerGrpc(this WebApplication app)
    {
        app.MapGrpcService<WorkerGrpcService>();
    }

    /// <summary>Maps the worker REST claim/heartbeat/complete/fail surface.</summary>
    /// <param name="app"></param>
    public static void MapWorkerRest(this WebApplication app)
    {
        WorkerEndpoints.MapWorkerEndpoints(app);
    }
}
