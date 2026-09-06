using Comuki.Modules.Knowledge.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Comuki.Modules.Knowledge.Infrastructure;

/// <summary>Registration entry point for Knowledge persistence.</summary>
public static class KnowledgePersistenceExtensions
{
    /// <summary>
    /// Registers <see cref="KnowledgeDbContext"/> (Npgsql + snake_case +
    /// private migrations history via <see cref="KnowledgeDbContext.ApplyOptions"/>).
    /// The Knowledge ingestor + searcher are singletons over the context
    /// factory — every operation opens its own context, so transactions
    /// scope to one call.
    /// </summary>
    /// <param name="services"></param>
    /// <param name="connectionString"></param>
    public static IServiceCollection AddKnowledgePersistence(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContextFactory<KnowledgeDbContext>(options =>
            KnowledgeDbContext.ApplyOptions(options, connectionString));

        return services;
    }
}
