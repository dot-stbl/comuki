using Comuki.Modules.Procedures.Application.Compiler;
using Comuki.Modules.Procedures.Application.Editions;
using Comuki.Modules.Procedures.Application.Patches;
using Comuki.Modules.Procedures.Application.Patches.Diffing;
using Comuki.Modules.Procedures.Application.Patches.Drafting;
using Comuki.Modules.Procedures.Application.Ports;
using Comuki.Modules.Procedures.Application.ProcedureVersions;
using Comuki.Modules.Procedures.Application.ProcedureVersions.Ledger;
using Comuki.Modules.Procedures.Application.Runtime.Trace.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace Comuki.Modules.Procedures.Application;

/// <summary>
/// Composition of the Procedures application layer: registers the compile
/// gate, the GraphPatch drafting/diff/publication services, and the
/// attempt-pin resolver/ledger with their Domain dependencies. The
/// versioned store (task 2.4), the outbox, and the file-backed catalog
/// reader are Infrastructure / host concerns — the Infrastructure
/// installer and the host wire those ports.
/// </summary>
public static class ProceduresApplicationExtensions
{
    /// <summary>
    /// Registers the Procedures application layer: the deterministic
    /// compile gate (task 2.3), the GraphPatch drafting/diff ports
    /// (task 3.1), the publication service that enforces the
    /// "brain proposes / human publishes" rule (task 3.2), and the
    /// attempt-pin resolver + in-memory ledger for retry re-pin
    /// (task 3.3). The Infrastructure installer wires the concrete
    /// <c>INodeKindCatalogReader</c>, <c>IEditionsFeatureSource</c>,
    /// <c>IProcedureVersionStore</c>, and the host wires
    /// <c>IOutbox</c>.
    /// </summary>
    /// <param name="services">The service collection the Procedures module is being composed into.</param>
    /// <param name="configuration">
    /// The host configuration. The <c>ControlPlane</c> section
    /// (<see cref="ProceduresOptions.SectionName"/>) feeds both this
    /// options class and the host's <c>ControlPlaneCatalogOptions</c>;
    /// the validation pass on the host's options covers the binding.
    /// </param>
    public static IServiceCollection AddProceduresApplication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<ProceduresOptions>()
            .Bind(configuration.GetSection(ProceduresOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IProcedureCompiler, ProcedureCompiler>();
        services.AddSingleton<IEditionsFeatureSource, CommunityEditionsFeatureSource>();
        services.AddSingleton<IGraphPatchDraftingService, GraphPatchDraftingService>();
        services.AddSingleton<IGraphPatchDiffService, GraphPatchDiffService>();
        services.AddSingleton<IPublicationService, PublicationService>();
        services.AddSingleton<IAttemptPinResolver, AttemptPinResolver>();
        services.AddSingleton<IAttemptPinLedger, InMemoryAttemptPinLedger>();
        services.AddSingleton<IProcedureTraceStore, InMemoryProcedureTraceStore>();
        return services;
    }
}
