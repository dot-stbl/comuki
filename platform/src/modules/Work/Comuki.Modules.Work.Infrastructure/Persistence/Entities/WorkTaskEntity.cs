namespace Comuki.Modules.Work.Infrastructure.Persistence.Entities;

/// <summary>
/// EF entity for the <c>work.work_tasks</c> row — a flat
/// representation of the <c>WorkTask</c> aggregate's persisted state.
/// The aggregate-side guards run on the domain type, not here; this
/// shape exists so the EF materialiser can hydrate side collections
/// (sources, dependencies) in one pass without poking at the
/// aggregate's private mutators. <see cref="Version"/> is the
/// optimistic-concurrency token (bumped by the store on every save
/// per <c>architecture.md §Persistence</c>).
/// </summary>
/// <param name="Id">UUIDv7 (per <c>WorkTaskId.New</c>).</param>
/// <param name="Title">Immutable after create (per <c>WorkTask</c> invariant).</param>
/// <param name="Brief">Current brief, mutable via <c>WorkTask.ReviseBrief</c>.</param>
/// <param name="AttemptOrdinal">Monotonic per-Task ordinal; <c>0</c> = no attempt yet.</param>
/// <param name="ResolutionOutcome">Set on the <c>→ Resolved</c> edge only.</param>
public sealed record WorkTaskEntity(
    Guid Id,
    Guid ProjectId,
    string Title,
    string Brief,
    int BriefVersion,
    int AttemptOrdinal,
    Guid? ActiveAttemptId,
    string Visibility,
    Guid? MissionId,
    string Status,
    string? ResolutionOutcome,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>EF materialisation ctor — the column-bound read path.</summary>
    public WorkTaskEntity() : this(
        Id: Guid.Empty,
        ProjectId: Guid.Empty,
        Title: string.Empty,
        Brief: string.Empty,
        BriefVersion: 1,
        AttemptOrdinal: 0,
        ActiveAttemptId: null,
        Visibility: "Project",
        MissionId: null,
        Status: "Draft",
        ResolutionOutcome: null,
        Version: 0,
        CreatedAt: default,
        UpdatedAt: default)
    {
    }
}
