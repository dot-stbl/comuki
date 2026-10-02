using Comuki.Modules.Procedures.Application.Ports;
using Comuki.Modules.Procedures.Application.ProcedureVersions;
using Comuki.Modules.Procedures.Infrastructure.Loading;
using Comuki.Modules.Procedures.Infrastructure.Persistence;
using Comuki.Modules.Procedures.Infrastructure.Persistence.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace Comuki.Modules.Procedures.Infrastructure;

/// <summary>
/// Composition of the Procedures Infrastructure layer: EF context
/// registration and the port implementations the Application layer
/// reaches through.
/// </summary>
public static class ProceduresInfrastructureExtensions
{
    /// <summary>
    /// Registers the Procedures Infrastructure: the EF context (snake_case,
    /// per-schema history table) and the immutable version store.
    /// </summary>
    /// <param name="services">The service collection being composed into.</param>
    /// <param name="connectionString">Postgres connection string.</param>
    public static IServiceCollection AddProceduresInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContextFactory<ProceduresDbContext>((serviceProvider, options) =>
            ProceduresDbContext.ApplyOptions(options, connectionString));

        services.AddSingleton<IProcedureVersionStore, ProcedureVersionStore>();
        services.AddSingleton<INodeKindCatalogReader, NodeKindCatalogReader>();
        return services;
    }
}
