using Comuki.Engine.Compute.Options;
using Comuki.Engine.Compute.Security;
using Comuki.Engine.Compute.Security.Stores;
using Comuki.Host.Workers.Api;
using Comuki.Host.Workers.Grpc;
using Comuki.Shared.Kernel.Scoping;
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

        // Per-execution proxy capability (issue #122): minted on claim,
        // revoked on complete/fail. IVirtualKeyStore + ProxyOptions come
        // from AddProxyApplication — every composition that maps the
        // worker REST surface registers both (the full host and the
        // worker test fixtures).
        services.AddSingleton<VirtualKeys.MintedVirtualKeyService>();

        // Source-git enrichment of the claim (harden-pi-worker-sandbox
        // 4.3): resolves the project's repository URL/ref and the
        // optional HTTPS credential. IProjectStore /
        // IProjectSettingsStore / ISecretResolver come from the Projects
        // module and the host's secret composition — resolved lazily, so
        // a fixture that maps the worker surface without the Projects
        // module only pays for this when it actually claims. Scoped: the
        // store dependencies are ProjectsDbContext-bound (request scope),
        // a singleton registration would capture the context.
        services.AddScoped<ClaimSourceGitResolver>();

        // The worker runtime is a system consumer by nature: the gRPC
        // service and the REST surface run every operation as
        // AsSystem("worker-runtime") and need the ambient-scope bus to do
        // it. TryAdd — the full host registers the same implementation.
        services.TryAddSingleton<ISubjectScopeAccessor, AsyncLocalSubjectScopeAccessor>();

        return services;
    }

    /// <summary>Maps the worker gRPC service and the worker REST endpoints.</summary>
    /// <param name="app"></param>
    /// <param name="workerGrpcPort">
    /// When set, scopes the gRPC endpoint to this port only (issue #152 —
    /// Program.cs's dedicated worker-gRPC listener). Null in every
    /// existing test fixture that composes its own single-purpose or
    /// split-listener host — those already dedicate the whole listener to
    /// gRPC (or a different port from REST), so the restriction is
    /// redundant there.
    /// </param>
    public static void MapWorkerRuntime(this WebApplication app, int? workerGrpcPort = null)
    {
        app.MapWorkerGrpc(workerGrpcPort);
        app.MapWorkerRest();
    }

    /// <summary>
    /// Maps only the bidi gRPC service. Hosts that register no orchestration
    /// application services (REST claim handlers) must use this overload —
    /// mapping REST without its handlers fails parameter binding per request.
    /// </summary>
    /// <param name="app"></param>
    /// <param name="workerGrpcPort">See <see cref="MapWorkerRuntime"/>.</param>
    public static void MapWorkerGrpc(this WebApplication app, int? workerGrpcPort = null)
    {
        var conventions = app.MapGrpcService<WorkerGrpcService>();
        if (workerGrpcPort is { } port)
        {
            conventions.RequireHost($"*:{port}");
        }
    }

    /// <summary>Maps the worker REST claim/heartbeat/complete/fail surface.</summary>
    public static void MapWorkerRest(this WebApplication app)
    {
        WorkerEndpoints.MapWorkerEndpoints(app);
        UploadArtifactEndpoint.MapUploadArtifactEndpoint(app);
    }
}
