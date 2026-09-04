using Microsoft.Extensions.DependencyInjection;

namespace Comuki.Modules.Knowledge.Application;

/// <summary>
/// Composition of the Knowledge application layer — no service
/// implementations live here, only the registration of ports so the
/// infrastructure installer can pick them up. Persistence ports
/// (<see cref="IKnowledgeIngestor"/>, <see cref="IKnowledgeSearcher"/>,
/// <see cref="IEmbeddingClient"/>) are satisfied by the infrastructure
/// installer.
/// </summary>
public static class KnowledgeApplicationExtensions
{
    /// <summary>Registers the Knowledge application services.</summary>
    /// <param name="services"></param>
    public static IServiceCollection AddKnowledgeApplication(this IServiceCollection services)
    {
        return services;
    }
}
