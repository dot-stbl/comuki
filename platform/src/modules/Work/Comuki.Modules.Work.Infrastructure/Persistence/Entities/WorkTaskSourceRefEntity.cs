namespace Comuki.Modules.Work.Infrastructure.Persistence.Entities;

/// <summary>
/// EF entity for <c>work.work_task_source_refs</c> — exactly one
/// primary per task (the <c>is_primary</c> flag, enforced by a
/// partial unique index on <c>(task_id, is_primary) WHERE
/// is_primary</c>). The aggregate side
/// (<c>WorkTask.AddSourceRef</c>) rejects a second primary on its
/// own; the unique index is a defence-in-depth guard.
/// </summary>
public sealed class WorkTaskSourceRefEntity(
    Guid id,
    Guid taskId,
    string kind,
    string externalId,
    string displayName,
    bool isPrimary,
    string? linkNote)
{
    public WorkTaskSourceRefEntity() : this(
        id: Guid.Empty,
        taskId: Guid.Empty,
        kind: string.Empty,
        externalId: string.Empty,
        displayName: string.Empty,
        isPrimary: false,
        linkNote: null)
    {
    }

    public Guid Id { get; set; } = id;
    public Guid TaskId { get; set; } = taskId;
    public string Kind { get; set; } = kind;
    public string ExternalId { get; set; } = externalId;
    public string DisplayName { get; set; } = displayName;
    public bool IsPrimary { get; set; } = isPrimary;
    public string? LinkNote { get; set; } = linkNote;
}
