using Comuki.Modules.Procedures.Domain.Patches.Model;
using Comuki.Modules.Procedures.Domain.Patches.Publication;

namespace Comuki.Modules.Procedures.Application.Patches.Helpers;

/// <summary>
/// Publish-rights check (design decision 5), extracted per the
/// no-private-methods rule: the brain cannot publish its own draft,
/// and the approver must be a distinct human identity from the drafter.
/// </summary>
internal static class PublicationRights
{
    /// <summary>
    /// Refuses a publication whose patch was drafted by the brain
    /// (a brain draft must be re-drafted as an operator or system
    /// draft first) or whose approver is the same identity as the
    /// drafter.
    /// </summary>
    /// <param name="request">The publication request under check.</param>
    public static void Enforce(PublicationRequest request)
    {
        if (request.Patch.DraftedBy.Kind == GraphPatchDraftedKind.Brain)
        {
            throw new PublicationException(
                PublicationException.BrainDraftNotPublishable,
                $"Patch '{request.Patch.Id}' was drafted by the brain (identity '{request.Patch.DraftedBy.Identity}'); the brain cannot publish its own draft. Re-draft this patch as an operator or system draft and try again.");
        }

        if (string.Equals(request.Approver, request.Patch.DraftedBy.Identity, StringComparison.Ordinal))
        {
            throw new PublicationException(
                PublicationException.ApproverSameAsDrafter,
                $"Patch '{request.Patch.Id}' was drafted by '{request.Patch.DraftedBy.Identity}'; the approver '{request.Approver}' is the same identity. A distinct human approver is required (design decision 5).");
        }
    }
}
