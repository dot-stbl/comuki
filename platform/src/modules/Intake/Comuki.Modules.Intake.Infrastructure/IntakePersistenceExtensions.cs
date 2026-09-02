using Comuki.Modules.Intake.Application.Ports.Sources;
using Comuki.Modules.Intake.Application.Ports.Tickets;
using Comuki.Modules.Intake.Infrastructure.Persistence;
using Comuki.Modules.Intake.Infrastructure.Persistence.Stores;
using Comuki.Modules.Intake.Infrastructure.Sync;
using Microsoft.Extensions.DependencyInjection;

namespace Comuki.Modules.Intake.Infrastructure;

/// <summary>Registration entry point for Intake persistence.</summary>
public static class IntakePersistenceExtensions
{
    /// <summary>
    /// Registers <see cref="IntakeDbContext"/> (Npgsql + snake_case +
    /// private migrations history via
    /// <see cref="IntakeDbContext.ApplyOptions"/>), the intake store
    /// (scoped — one context per unit of work), the env secret resolver
    /// and the run status bridge worker.
    /// </summary>
    /// <param name="services"></param>
    /// <param name="connectionString"></param>
    /// <returns></returns>
    public static IServiceCollection AddIntakePersistence(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContextFactory<IntakeDbContext>(options =>
            IntakeDbContext.ApplyOptions(options, connectionString));

        services.AddScoped<IIntakeStore, IntakeStore>();
        services.AddSingleton<ISecretResolver, EnvSecretResolver>();
        services.AddHostedService<RunStatusBridgeWorker>();

        return services;
    }
}
