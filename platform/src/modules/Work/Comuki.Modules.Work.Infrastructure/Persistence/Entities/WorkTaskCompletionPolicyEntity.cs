namespace Comuki.Modules.Work.Infrastructure.Persistence.Entities;

/// <summary>
/// EF entity for <c>work.work_task_completion_policies</c> — a
/// per-Task completion policy with a versioned evidence contract.
/// A new policy version is created on every change
/// (<c>work_management/spec.md §"Task completion policy"</c>); the
/// <c>Resolve</c> command always reads the policy version active
/// at the time of the attempt's terminal, not the current version.
/// </summary>
public sealed record WorkTaskCompletionPolicyEntity(
    Guid Id,
    Guid TaskId,
    int Version,
    string PolicyKind,
    string EvidenceContractJson,
    DateTimeOffset CreatedAt)
{
    public WorkTaskCompletionPolicyEntity() : this(
        Id: Guid.Empty,
        TaskId: Guid.Empty,
        Version: 1,
        PolicyKind: string.Empty,
        EvidenceContractJson: "[]",
        CreatedAt: default)
    {
    }
}
