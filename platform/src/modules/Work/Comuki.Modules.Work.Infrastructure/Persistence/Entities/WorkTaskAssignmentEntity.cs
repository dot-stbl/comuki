namespace Comuki.Modules.Work.Infrastructure.Persistence.Entities;

/// <summary>
/// EF entity for <c>work.work_task_assignments</c> — a Task's
/// responsible-actor metadata (one row per actor). The aggregate
/// side treats these as attention metadata only; authorization is
/// the <c>RequiresPermission</c> pipeline's job (per
/// <c>add-work-management/specs/work-management/spec.md</c>
/// Requirement "Responsible actors as attention metadata"). A
/// service-actor assignment carries an optional
/// <c>capacity_hint</c>; a human assignment is gated by
/// <c>proposal_required</c> and a distinct-human approval.
/// </summary>
public sealed record WorkTaskAssignmentEntity(
    Guid Id,
    Guid TaskId,
    string ActorKind,
    string ActorId,
    int? CapacityHint,
    bool ProposalRequired,
    string? ProposalState,
    string? ProposedBy,
    DateTimeOffset AssignedAt)
{
    public WorkTaskAssignmentEntity() : this(
        Id: Guid.Empty,
        TaskId: Guid.Empty,
        ActorKind: string.Empty,
        ActorId: string.Empty,
        CapacityHint: null,
        ProposalRequired: false,
        ProposalState: null,
        ProposedBy: null,
        AssignedAt: default)
    {
    }
}
