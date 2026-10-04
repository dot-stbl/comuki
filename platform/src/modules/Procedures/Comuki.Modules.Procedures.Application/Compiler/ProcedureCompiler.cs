using Comuki.Modules.Procedures.Application.Compiler.Model;
using Comuki.Modules.Procedures.Application.Compiler.Validation;
using Comuki.Modules.Procedures.Application.Compiler.Versioning;
using Comuki.Modules.Procedures.Application.Ports;
using Comuki.Modules.Procedures.Domain.Editions;
using Microsoft.Extensions.Options;
namespace Comuki.Modules.Procedures.Application.Compiler;

/// <summary>
/// Deterministic compile gate: validates a procedure graph against the
/// pinned node-kind catalog, enforces editions grants, checks structural
/// invariants (DAG, port resolution, fan-out ceiling), and produces an
/// immutable content-addressed version. Pure — identical inputs always
/// produce an identical <see cref="CompiledProcedureVersion"/> (spec
/// requirement "Deterministic compile gate"). The structural checks
/// live in <see cref="CompileGraphValidation"/>, the version
/// artifact in <see cref="CompileVersioning"/>.
/// </summary>
/// <param name="catalogReader">Loads the pinned node-kind catalog.</param>
/// <param name="editionsSource">Resolves the effective edition's granted feature keys.</param>
/// <param name="options">
/// Module options — carries the absolute path of the control-plane root
/// (<c>ControlPlane:Root</c>). The host binds the same config section
/// the <c>ControlPlaneCatalog</c> binds; one key feeds both readers.
/// </param>
public sealed class ProcedureCompiler(
    INodeKindCatalogReader catalogReader,
    IEditionsFeatureSource editionsSource,
    IOptions<ProceduresOptions> options) : IProcedureCompiler
{
    /// <summary>Maximum nodes at any single depth level.</summary>
    public const int FanOutCeiling = 10;

    /// <summary>Source-root folder name the pinned node-kind catalog loads from.</summary>
    public const string ControlPlaneRootName = "control-plane";

    /// <summary>Refusal code for the empty ControlPlaneRoot case.</summary>
    public const string ControlPlaneRootMissing = "procedures.compiler.control_plane_root_missing";

    /// <inheritdoc />
    public async Task<CompiledProcedureVersion> CompileAsync(
        ProcedureDefinition definition,
        CancellationToken cancellationToken = default)
    {
        var controlPlaneRoot = options.Value.ControlPlaneRoot;
        if (string.IsNullOrWhiteSpace(controlPlaneRoot))
        {
            throw new ProcedureCompilerException(
                ControlPlaneRootMissing,
                "ProcedureCompiler.ControlPlaneRoot is not configured. Set the ControlPlane:Root config key to the absolute path of the control-plane content root.");
        }

        var catalog = await catalogReader.LoadAsync(
            controlPlaneRoot,
            cancellationToken);

        NodeKindEditionsGate.ValidateEntries(catalog.Entries, editionsSource.ResolveEffective());
        CompileGraphValidation.ValidateGraph(
            definition.Graph,
            CompileGraphValidation.BuildKindLookup(catalog));

        return new CompiledProcedureVersion(
            CompileVersioning.ComputeVersionId(definition, catalog),
            definition.ProjectId,
            definition.ProcedureKey,
            catalog.Version,
            definition.GitRef,
            CompileVersioning.SerializeGraph(definition.Graph));
    }
}
