using Comuki.Modules.Procedures.Domain.Ids;
using Comuki.Modules.Procedures.Domain.Patches;

namespace Comuki.Modules.Procedures.Application.Patches.Drafting;

/// <summary>
/// Default <see cref="IGraphPatchDraftingService"/> implementation:
/// stamps the durable proposal object and returns it. The service is
/// deliberately thin — it does not call the validator, the diff
/// computer, or the version store. The brain's draft is a value the
/// chat surface (task 8.1) keeps; the diff path is opt-in.
/// </summary>
public sealed class GraphPatchDraftingService(TimeProvider clock) : IGraphPatchDraftingService
{
    /// <inheritdoc />
    public Task<GraphPatch> DraftAsync(
        DraftGraphPatchRequest request,
        CancellationToken cancellationToken = default)
    {
        var patch = new GraphPatch(
            Id: GraphPatchId.New(),
            BaseVersionId: request.BaseVersionId,
            ProjectId: request.ProjectId,
            ProcedureKey: request.ProcedureKey,
            Operations: request.Operations,
            Rationale: request.Rationale ?? string.Empty,
            DraftedBy: request.DraftedBy,
            DraftedAt: clock.GetUtcNow());

        return Task.FromResult(patch);
    }
}
