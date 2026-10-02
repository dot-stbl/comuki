namespace Comuki.Modules.Procedures.Domain.Patches.Model;

/// <summary>
/// The closed set of actor classes that may draft a
/// <see cref="GraphPatch"/>. The brain (chat) is the primary drafter per
/// design decision 5 ("brain proposes patches, humans publish"); an
/// operator may also draft a patch directly through Studio; <c>System</c>
/// covers automation flows that originate in the platform itself (e.g.
/// a migration script attaching a new node to every procedure). Wired-form
/// lowercase for the storage shape so the JSON serialization and the
/// capability-broker exposure class share the same identifier.
/// </summary>
public enum GraphPatchDraftedKind
{
    /// <summary>Drafted by the brain (chat) on behalf of an operator.</summary>
    Brain,

    /// <summary>Drafted directly by a human in Studio.</summary>
    Operator,

    /// <summary>Drafted by platform automation (not an operator-visible action).</summary>
    System,
}

/// <summary>
/// Identity of the actor that drafted a <see cref="GraphPatch"/>: who
/// (the <see cref="Identity"/> string the auth layer carries) and how
/// (<see cref="Kind"/>). The brain is always <see cref="GraphPatchDraftedKind.Brain"/>
/// + a session id; an operator is <see cref="GraphPatchDraftedKind.Operator"/>
/// + their user id. The draft lifecycle does not change across the
/// proposal — only the publication step at task 3.2 swaps the actor to
/// the human approver.
/// </summary>
/// <param name="Identity">
/// Stable identity of the actor (brain session id, operator user id,
/// or automation job id). Used for audit replay alongside the patch.
/// </param>
/// <param name="Kind">How the actor relates to the patch draft flow.</param>
public sealed record GraphPatchDraftedBy(
    string Identity,
    GraphPatchDraftedKind Kind);
