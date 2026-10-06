using Comuki.Engine.Orchestration.Infrastructure.Verification;
using Comuki.Modules.Verify.Infrastructure.Verification;
using Comuki.Shared.Contracts.Verification;
using Comuki.Shared.Editions.Gating;

namespace Comuki.Host.Verification;

/// <summary>
/// Host extension that wires the verification axis
/// (add-orchestra §3 — Coda): the engine scaffolding, the per-project
/// <c>VerifyEnabled</c> adapter, and the platform-shipped first
/// gate provider (<see cref="GenericCommandGateProvider"/>). The
/// <c>[RequiresFeature(Features.Verification)]</c> attribute on
/// <see cref="AddOrchestrationVerification"/> is the indexable
/// call-site for the <c>verification</c> paid feature key — the
/// architecture test
/// <c>EveryPaidRegistryEntryIsGatedShould</c> walks
/// <c>Comuki.Host</c>'s public surface and confirms the gate is
/// present; the Community edition answers 403 here until the
/// feature flips on.
/// </summary>
public static class VerificationHostExtensions
{
    /// <summary>
    /// Wires the orchestration verification scaffolding plus the
    /// platform-shipped first gate provider.
    /// </summary>
    /// <param name="services">The host's service collection.</param>
    /// <param name="configuration">The host configuration.</param>
    [RequiresFeature("verification")]
    public static IServiceCollection AddOrchestrationVerification(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Engine scaffolding: record store + provider registry +
        // evaluation service. The engine wires the
        // IRunJournal / IWorkItemQueue injection points so the
        // evaluation can stamp the gate.evaluated event in the same
        // transaction as the work-item terminalization.
        services.AddOrchestrationVerificationCore(configuration);

        // Per-project VerifyEnabled adapter — the engine never
        // references the Projects module; the host composes a thin
        // shim over the existing IProjectSettingsStore cache.
        services.AddSingleton<IProjectVerificationSettings, ProjectVerificationSettingsAdapter>();

        // Platform-shipped first provider: the existing Verify
        // module's GenericCommandGateProvider. Reads the verdict the
        // GenericCommandVerifierWorker has already stamped and
        // forwards it to the verification axis.
        services.AddVerificationGateProvider<GenericCommandGateProvider>();

        return services;
    }
}

