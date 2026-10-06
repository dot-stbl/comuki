using Comuki.Engine.Orchestration.Application.Verification;
using Comuki.Engine.Orchestration.Infrastructure.Persistence.Stores;
using Comuki.Shared.Contracts.Verification;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Comuki.Engine.Orchestration.Infrastructure.Verification;

/// <summary>
/// Host extension that registers the verification scaffolding
/// (add-orchestra §3 — Coda): the record store, the provider
/// registry, the evaluation service, and a typed
/// <see cref="AddVerificationGateProvider{T}"/> helper the host
/// composition root uses to wire each provider. Provider authors
/// register their own <c>IVerificationGateProvider</c> implementation
/// through this extension — no platform code changes for new gates
/// (the registry iteration handles the rest).
/// </summary>
public static class VerificationHostExtensions
{
    /// <summary>
    /// Wires the verification scaffolding: the EF-backed record store,
    /// the singleton provider registry, the scoped evaluation service
    /// and the <see cref="VerificationOptions"/> binding. The
    /// platform-shipped first provider (the Verify module's
    /// GenericCommandGateProvider, wired by the host) and any
    /// operator-written provider go through
    /// <see cref="AddVerificationGateProvider{T}"/> below.
    /// </summary>
    /// <param name="services">The host's service collection.</param>
    /// <param name="configuration">The host configuration (binds <c>Orchestration:Verification</c>).</param>
    public static IServiceCollection AddOrchestrationVerificationCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<VerificationOptions>()
            .Bind(configuration.GetSection(VerificationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<IVerificationRecordStore, VerificationRecordStoreEf>();
        services.AddScoped<VerificationEvaluationService>();
        services.TryAddSingleton<IVerificationProviderRegistry, VerificationProviderRegistry>();
        return services;
    }

    /// <summary>
    /// Registers <typeparamref name="TProvider"/> in the gate-provider
    /// registry. <typeparamref name="TProvider"/> is constructed
    /// through DI; the helper only wires the <c>IVerificationGateProvider</c>
    /// index entry. The platform throws at composition if two
    /// providers declare the same <c>GateName</c> — that is the duplicate
    /// gate-name guard the spec requires.
    /// </summary>
    /// <typeparam name="TProvider">Concrete provider type; must implement <see cref="IVerificationGateProvider"/>.</typeparam>
    /// <param name="services">The host's service collection.</param>
    public static IServiceCollection AddVerificationGateProvider<TProvider>(this IServiceCollection services)
        where TProvider : class, IVerificationGateProvider
    {
        // Singleton + keyed registration: the registry snapshots the
        // DI-resolved instance once at composition, so the provider's
        // lifetime must match (singleton). Provider authors get a typed
        // constructor — DI handles the rest.
        services.AddSingleton<TProvider>();
        services.AddSingleton<IVerificationGateProvider>(static serviceProvider =>
            serviceProvider.GetRequiredService<TProvider>());
        return services;
    }
}
