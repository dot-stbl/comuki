namespace Comuki.Modules.Work.Domain.Assignments;

/// <summary>
/// One responsible-actor metadata row on a <see cref="WorkTask"/>.
/// The umbrella's task 7.3 invariant: assignment does NOT grant
/// authorization (the <c>RequiresPermission</c> pipeline is unchanged);
/// the assignment is audited and surfaces on the read projection.
/// </summary>
public sealed record WorkTaskAssignment(
    Guid Id,
    AssignmentActorKind ActorKind,
    string ActorId,
    int? CapacityHint,
    bool ProposalRequired,
    AssignmentProposalState ProposalState,
    string? ProposedBy,
    DateTimeOffset AssignedAt);
