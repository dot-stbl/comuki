using Comuki.Engine.Orchestration.Infrastructure.Verification;
using Comuki.Modules.Verify.Application.Options;
using Comuki.Modules.Verify.Infrastructure.Verification;
using Comuki.Shared.Contracts.Verification;

namespace Comuki.Host.Verification;

/// <summary>
/// Host extension that wires the verification axis
/// (add-orchestra §3 — Coda): the engine scaffolding, the per-project
/// <c>VerifyEnabled</c> adapter, the platform-shipped first
/// gate provider (<see cref="GenericCommandGateProvider"/>), and the
/// producer-side options the gate reads. The
/// <c>[RequiresFeature(Features.Verification)]</c> attribute is
/// attached to the verification view endpoint
/// (<c>RunsController.GetVerificationAsync</c>) — that endpoint
/// is the only consumer-side gate. The architecture test
/// <c>EveryPaidRegistryEntryIsGatedShould</c> walks
/// <c>Comuki.Host</c>'s public surface and confirms the gate is
/// present; the Community edition answers 403
/// <c>edition.feature_unavailable</c> on the endpoint until the
/// feature flips on. The composition-root extension itself is
/// unrestricted — it must run on every contour so the engine
/// scaffolding, the <c>VerifyEnabled</c> adapter, and the platform-shipped
/// provider are always wired; the gate is per-endpoint, not per-wire.
/// </summary>
public static class VerificationHostExtensions
{
    /// <summary>
    /// Wires the orchestration verification scaffolding plus the
    /// platform-shipped first gate provider. Unrestricted — see the
    /// type-level doc on this class for the endpoint-level gate.
    /// </summary>
    /// <param name="services">The host's service collection.</param>
    /// <param name="configuration">The host configuration.</param>
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

        // Producer-side options: GenericCommandGateProvider's
        // EnsureGateRunAsync reads [Orchestration:Verification:CommandGate]
        // and inserts a GenericCommandRun on the first evaluation
        // pass when the section is bound. Empty section = producer
        // stays off (the gate stamps Pending and the operator
        // schedules runs by hand); ValidateDataAnnotations +
        // ValidateOnStart fail the boot on a half-set pair.
        services.AddOptions<CommandGateOptions>()
            .Bind(configuration.GetSection(CommandGateOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

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

