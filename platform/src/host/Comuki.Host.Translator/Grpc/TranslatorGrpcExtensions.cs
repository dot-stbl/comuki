using Comuki.Shared.Contracts.Grpc;
using Grpc.Net.Client;
using Microsoft.Extensions.Options;
using ProtoBuf.Grpc.Client;

namespace Comuki.Host.Translator.Grpc;

/// <summary>
/// Registers the code-first gRPC client for <see cref="IWorkerService"/>:
/// one channel to the orchestrator, one client proxy, both singletons for
/// the container's lifetime.
/// </summary>
public static class TranslatorGrpcExtensions
{
    /// <summary>Adds the worker gRPC channel and <see cref="IWorkerService"/> client.</summary>
    /// <param name="services"></param>
    public static IServiceCollection AddWorkerGrpcClient(this IServiceCollection services)
    {
        services.AddSingleton(static serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<TranslatorOptions>>().Value;
            return GrpcClientFactory.CreateGrpcService<IWorkerService>(
                GrpcChannel.ForAddress(options.OrchestratorGrpcUrl));
        });
        return services;
    }
}
