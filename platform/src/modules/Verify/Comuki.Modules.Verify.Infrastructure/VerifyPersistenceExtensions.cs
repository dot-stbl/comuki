using Comuki.Modules.Verify.Application.Ports;
using Comuki.Modules.Verify.Infrastructure.Persistence;
using Comuki.Modules.Verify.Infrastructure.Persistence.Stores;
using Comuki.Modules.Verify.Infrastructure.Sync;
using Comuki.Shared.Bootstrap.Workers;
using Microsoft.Extensions.DependencyInjection;

namespace Comuki.Modules.Verify.Infrastructure;

/// <summary>Registration entry point for Verify persistence + the verifier worker.</summary>
public static class VerifyPersistenceExtensions
{
    /// <summary>
    /// Registers <see cref="VerifyDbContext"/> (Npgsql + snake_case +
    /// private migrations history via <see cref="VerifyDbContext.ApplyOptions"/>),
    /// the generic-command store (scoped — one context per unit of work),
    /// the in-process process runner, and the verifier worker behind the
    /// comuki worker registry (a host that runs it must also call
    /// <c>AddComukiWorkers()</c>). <see cref="IGenericCommandRunner"/>
    /// stays a port so the v1.1 container-isolated runner (GH issue #47)
    /// swaps in without touching the worker.
    /// </summary>
    /// <param name="services">The host's service collection.</param>
    /// <param name="connectionString">Postgres connection string.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddVerifyPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContextFactory<VerifyDbContext>(options =>
            VerifyDbContext.ApplyOptions(options, connectionString));

        services.AddScoped<IGenericCommandStore, GenericCommandStore>();
        services.AddScoped<IGenericCommandRunner, GenericCommandProcessRunner>();

        services.AddSingleton<IComukiWorker, GenericCommandVerifierWorker>();

        return services;
    }
}
