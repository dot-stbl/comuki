using Comuki.Modules.Procedures.Application.Patches.Helpers;
using Comuki.Modules.Procedures.Application.ProcedureVersions;
using Comuki.Modules.Procedures.Domain.Patches;
using Comuki.Modules.Procedures.Domain.Patches.Diff;

namespace Comuki.Modules.Procedures.Application.Patches.Diffing;

/// <summary>
/// Default <see cref="IGraphPatchDiffService"/> implementation: loads
/// the base compiled version, deserializes its graph, validates the
/// patch, and computes the semantic diff. Pure orchestration — the
/// domain validator and diff computer are the units under test at
/// task 3.1; this service is the wiring that reaches them through the
/// version store. Graph deserialization lives in
/// <see cref="GraphJson"/>.
/// </summary>
/// <param name="versionStore">The compiled-version store the host wired at task 2.4.</param>
public sealed class GraphPatchDiffService(IProcedureVersionStore versionStore) : IGraphPatchDiffService
{
    /// <inheritdoc />
    public async Task<GraphPatchDiff> ComputeDiffAsync(
        GraphPatch patch,
        CancellationToken cancellationToken = default)
    {
        var baseVersion = await versionStore.GetAsync(patch.BaseVersionId, cancellationToken) ?? throw new GraphPatchException(
                GraphPatchException.InvalidBaseVersion,
                $"Patch base version '{patch.BaseVersionId}' does not match any compiled version for procedure '{patch.ProcedureKey}' in project '{patch.ProjectId}'.");
        var baseGraph = GraphJson.Deserialize(baseVersion.GraphJson);
        GraphPatchValidator.Validate(baseGraph, patch);
        return GraphPatchDiffComputer.Compute(baseGraph, patch);
    }
}
