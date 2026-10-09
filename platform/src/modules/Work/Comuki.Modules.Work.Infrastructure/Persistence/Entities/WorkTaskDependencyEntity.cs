namespace Comuki.Modules.Work.Infrastructure.Persistence.Entities;

/// <summary>
/// EF entity for <c>work.work_task_dependencies</c> — outgoing
/// dependency edges of the <c>WorkTask</c> aggregate. The aggregate
/// side rejects duplicate (prerequisite, kind) pairs and self-edges;
/// the unique index <c>ux_work_task_dependencies_prereq_kind</c> on
/// <c>(task_id, prerequisite_id, kind)</c> is the database-side
/// arbiter.
/// </summary>
public sealed record WorkTaskDependencyEntity(
    Guid Id,
    Guid TaskId,
    Guid PrerequisiteId,
    string Kind,
    bool CrossMission)
{
    public WorkTaskDependencyEntity() : this(
        Id: Guid.Empty,
        TaskId: Guid.Empty,
        PrerequisiteId: Guid.Empty,
        Kind: string.Empty,
        CrossMission: false)
    {
    }
}
