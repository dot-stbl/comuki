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
    /// Wires the verification scaffolding the engine owns: the
    /// <see cref="VerificationOptions"/> binding, the EF-backed record
    /// store, the scoped evaluation service, the singleton provider
    /// registry, and a default
    /// <see cref="IProjectVerificationSettings"/> so the engine
    /// composition validates on its own (the host's
    /// <c>ProjectVerificationSettingsAdapter</c> overrides this default
    /// — the last singleton registration for the port wins). The
    /// platform-shipped first provider (the Verify module's
    /// GenericCommandGateProvider, wired by the host) and any
    /// operator-written provider go through
    /// <see cref="AddVerificationGateProvider{T}"/> below.
    /// </summary>
    /// <param name="services">The service collection the engine composition builds.</param>
    /// <param name="configuration">Configuration (binds <c>Orchestration:Verification</c>).</param>
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
        // The engine ships an always-false default so the engine
        // composition validates on its own (the DiComposition unit
        // test builds only the engine composition and needs
        // IProjectVerificationSettings resolvable). The host's
        // ProjectVerificationSettingsAdapter overrides this default
        // through its own AddSingleton call — TryAddSingleton makes
        // the composition order irrelevant: whichever contour wires
        // last wins, and the engine's default never leaks into a host
        // composition.
        services.TryAddSingleton<IProjectVerificationSettings, DefaultProjectVerificationSettings>();
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
        // Singleton registration + same-singleton-both-sides binding:
        // the registry snapshots the DI-resolved instance once at
        // composition, so the provider's lifetime must match
        // (singleton). The same-singleton factory keeps concrete and
        // IVerificationGateProvider resolving to one instance — this
        // is the standard concrete-plus-interface pattern, not a
        // keyed service registration (the gate-name uniqueness is
        // enforced in VerificationProviderRegistry's ctor, not via
        // Microsoft.Extensions.DependencyInjection's keyed-service
        // feature). Provider authors get a typed constructor — DI
        // handles the rest.
        services.AddSingleton<TProvider>();
        services.AddSingleton<IVerificationGateProvider>(static serviceProvider =>
            serviceProvider.GetRequiredService<TProvider>());
        return services;
    }
}
