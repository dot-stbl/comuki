using Comuki.Shared.Contracts.Brain;
using Grpc.Net.Client;
using Microsoft.Extensions.Options;
using ProtoBuf.Grpc.Client;

namespace Comuki.Host.Chat.Brain;

/// <summary>
/// Selects the brain port: the gRPC client when <c>brain:endpoint</c> is
/// configured, nothing when it is not — the composition's
/// <c>TryAddSingleton&lt;IBrainClient, BrainStub&gt;</c> below this call
/// then wins and the host boots without a brain process. Swapping the
/// backend is this one branch; no other file knows which port is live.
/// </summary>
public static class ChatBrainExtensions
{
    /// <summary>Registers the gRPC brain client when an endpoint is configured.</summary>
    /// <param name="services"></param>
    /// <param name="configuration">Host configuration (the <c>[brain]</c> section).</param>
    public static IServiceCollection AddChatBrainClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(BrainClientOptions.SectionName);

        if (string.IsNullOrWhiteSpace(section[BrainClientOptions.EndpointKey]))
        {
            return services;
        }

        services.AddOptions<BrainClientOptions>()
            .Bind(section)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // One channel and one proxy for the process lifetime — the same
        // recipe the Translator uses for the worker protocol.
        services.AddSingleton(static serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<BrainClientOptions>>().Value;
            return GrpcClientFactory.CreateGrpcService<IBrainService>(GrpcChannel.ForAddress(options.Endpoint));
        });

        services.AddSingleton<BrainGrpcClient>();
        services.AddSingleton<IBrainClient>(static serviceProvider =>
            serviceProvider.GetRequiredService<BrainGrpcClient>());

        return services;
    }
}
