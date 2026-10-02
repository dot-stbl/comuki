namespace Comuki.Modules.Procedures.Domain.Patches.Publication;

/// <summary>
/// The policy context a <see cref="PublicationSurfaceValidator"/> needs
/// to decide whether a patch tries to widen its own surface (design
/// decision 5: "patches touching publish rights, autonomy ceilings, or
/// budget maxima are refused before compile"). Carries the procedure's
/// effective floors (from the layered merge at task 2.2) and the closed
/// set of kind keys the platform grants this procedure — anything the
/// validator needs to refuse a patch that would let a brain draft
/// publish its own policy change.
/// 
/// <para>
/// The <see cref="AllowedKindKeys"/> set comes from the catalog the
/// compile gate resolved against; the floors come from the
/// layered-merge step that produced the base version. Both are stable
/// for the lifetime of the patch: the validator refuses patches that
/// try to alter them; the publication service refuses patches that try
/// to bypass them by changing who approves the publication.
/// </para>
/// </summary>
/// <param name="AllowedKindKeys">
/// Closed set of catalog kind keys the procedure may instance. An
/// <c>AddNode</c> op whose kind key is not in this set is a forbidden
/// surface — the platform never authorized the procedure to add it.
/// </param>
/// <param name="MaxGenerations">
/// Floor on repair-boundary generations. An <c>AddNode</c> or
/// <c>ReParameterizeNode</c> op on a <c>repair-boundary</c> kind whose
/// <c>max-generations</c> parameter exceeds this value is a budget
/// violation.
/// </param>
/// <param name="MinApprovals">
/// Floor on human-gate approvals. An <c>AddNode</c> or
/// <c>ReParameterizeNode</c> op on a <c>human-gate</c> kind whose
/// <c>approvals</c> parameter is below this value is an autonomy
/// violation.
/// </param>
public sealed record PublicationContext(
    IReadOnlySet<string> AllowedKindKeys,
    int MaxGenerations,
    int MinApprovals);
