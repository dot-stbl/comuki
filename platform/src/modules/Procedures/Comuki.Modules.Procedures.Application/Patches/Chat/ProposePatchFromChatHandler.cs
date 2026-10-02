using Comuki.Modules.Procedures.Application.Patches.Diffing;
using Comuki.Modules.Procedures.Application.Patches.Drafting;

namespace Comuki.Modules.Procedures.Application.Patches.Chat;

/// <summary>
/// Task 8.1: the chat-facing propose-patch handler. The brain calls
/// this when an operator asks for a procedure change in chat. It wraps
/// the GraphPatchDraftingService and enforces the "brain proposes, never
/// publishes" boundary — the returned patch is a draft with no publish
/// path (spec: "the brain produces a draft GraphPatch with a rendered
/// diff and cannot publish it").
/// </summary>
/// <param name="draftingService">The Domain drafting service.</param>
/// <param name="diffService">Computes the semantic diff for Studio rendering.</param>
public sealed class ProposePatchFromChatHandler(
    IGraphPatchDraftingService draftingService,
    IGraphPatchDiffService diffService)
{
    /// <summary>
    /// Produces a GraphPatch draft from a chat-side request and renders
    /// its diff. The patch is returned with its semantic diff for Studio
    /// display — the chat surface has no publication path.
    /// </summary>
    /// <param name="request">The operator's intent (what to change, why).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The draft patch and its rendered diff.</returns>
    public async Task<ProposedPatch> HandleAsync(
        DraftGraphPatchRequest request,
        CancellationToken cancellationToken = default)
    {
        var patch = await draftingService.DraftAsync(request, cancellationToken);
        var diff = await diffService.ComputeDiffAsync(patch, cancellationToken);

        return new ProposedPatch(patch, diff);
    }
}
