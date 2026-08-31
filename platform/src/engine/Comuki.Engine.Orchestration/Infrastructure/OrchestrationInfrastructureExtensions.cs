using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Comuki.Engine.Orchestration.Infrastructure;

/// <summary>Registration entry point for orchestration persistence.</summary>
public static class OrchestrationInfrastructureExtensions
{
    /// <summary>
    /// Registers <see cref="OrchestrationDbContext"/> (Npgsql + snake_case
    /// via <see cref="OrchestrationDbContext.ApplyOptions"/>). Scoped — one
    /// context per unit of work / request.
    /// </summary>
    /// <param name="services"></param>
    /// <param name="connectionString"></param>
    public static IServiceCollection AddOrchestrationPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        _ = services.AddDbContext<OrchestrationDbContext>(options =>
            OrchestrationDbContext.ApplyOptions(options, connectionString));
        return services;
    }
}
