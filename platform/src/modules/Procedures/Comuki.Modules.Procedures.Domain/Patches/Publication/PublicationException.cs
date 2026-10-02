using Comuki.Modules.Procedures.Domain.Patches.Model;
using Comuki.Shared.Kernel.Exceptions;

namespace Comuki.Modules.Procedures.Domain.Patches.Publication;

/// <summary>
/// Typed refusal from the <see cref="PublicationSurfaceValidator"/> or
/// the publication service in the Application layer. Stable codes map
/// to distinct operator actions; the message always names the
/// offending element so a human approver (and Studio's rejection
/// badge) can see the reason without re-running the publication. Maps
/// to HTTP 422 via the shared <see cref="DomainException"/> handler —
/// semantic refusal, not an upstream 502.
/// </summary>
/// <param name="code">Stable dot.case identifier.</param>
/// <param name="message">Safe human message — names the offending element.</param>
public sealed class PublicationException(string code, string message)
    : DomainException(code, message)
{
    /// <summary>
    /// A patch's <c>AddNode</c> op references a kind key the platform's
    /// <see cref="PublicationContext.AllowedKindKeys"/> does not grant.
    /// (Design decision 5: "patches touching publish rights, autonomy
    /// ceilings, or budget maxima are refused before compile".)
    /// </summary>
    public const string KindNotAllowed = "procedures.publication.kind_not_allowed";

    /// <summary>
    /// A patch tries to set a <c>human-gate</c> node's
    /// <c>approvals</c> parameter below the procedure's
    /// <see cref="PublicationContext.MinApprovals"/> floor — the patch
    /// is trying to lower its own autonomy ceiling.
    /// </summary>
    public const string AutonomyBelowFloor = "procedures.publication.autonomy_below_floor";

    /// <summary>
    /// A patch tries to set a <c>repair-boundary</c> node's
    /// <c>max-generations</c> parameter above the procedure's
    /// <see cref="PublicationContext.MaxGenerations"/> floor — the
    /// patch is trying to widen its own budget.
    /// </summary>
    public const string BudgetAboveFloor = "procedures.publication.budget_above_floor";

    /// <summary>
    /// A patch's <c>AddNode</c> op adds a <c>human-gate</c> or
    /// <c>repair-boundary</c> node without the required
    /// <c>approvals</c> / <c>max-generations</c> parameter — the
    /// missing value defaults to zero (or whatever the parameter
    /// dictionary's natural zero is), which is below the floor.
    /// </summary>
    public const string PolicyParameterMissing = "procedures.publication.policy_parameter_missing";

    /// <summary>
    /// The human approver identity matches the patch's drafter
    /// identity. The brain (and the patch's operator) cannot publish
    /// its own draft; a distinct human approver is required (design
    /// decision 5: "brain proposes patches, humans publish").
    /// </summary>
    public const string ApproverSameAsDrafter = "procedures.publication.approver_same_as_drafter";

    /// <summary>
    /// The patch's draft kind is one publication does not accept — only
    /// <see cref="GraphPatchDraftedKind.Operator"/> and
    /// <see cref="GraphPatchDraftedKind.System"/> drafts can reach the
    /// publish step. Brain drafts must be re-drafted as operator or
    /// system drafts (the chat surface at task 8.1 is the entry point
    /// that turns a brain draft into a publishable one).
    /// </summary>
    public const string BrainDraftNotPublishable = "procedures.publication.brain_draft_not_publishable";

    /// <summary>
    /// The base version id named on the publication request does not
    /// match a stored compiled version for the (project, procedureKey)
    /// pair. The publication service surfaces this as a typed refusal
    /// rather than letting the compile gate fail downstream.
    /// </summary>
    public const string InvalidBaseVersion = "procedures.publication.invalid_base_version";

    /// <summary>
    /// The publication request's approver identity arrived empty or
    /// whitespace — caller-side bug, refuse loudly.
    /// </summary>
    public const string ApproverEmpty = "procedures.publication.approver_empty";
}
