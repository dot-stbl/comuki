using Comuki.Modules.Work.Domain;
using Comuki.Modules.Work.Domain.Attempts;
using Comuki.Modules.Work.Domain.Dependencies;
using Comuki.Modules.Work.Domain.Ids;
using Comuki.Modules.Work.Domain.Sources;
using Comuki.Modules.Work.Domain.Visibility;
using Comuki.Modules.Work.Infrastructure.Persistence.Entities;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Work.Infrastructure.Persistence;

/// <summary>
/// Flat ↔ aggregate translation. Lives in <c>Infrastructure</c>
/// because the EF entity shape is a persistence concern; the
/// reverse side runs through the domain's
/// <c>WorkTask</c> internal rehydrate ctor (covered by
/// <c>InternalsVisibleTo</c>). Smart-type ↔ wire-string conversion
/// stays here too — the domain types carry their own
/// <see cref="WorkTaskStatus.Value"/> / <see cref="TaskVisibility"/>
/// form, the entities store the SQL <c>varchar</c> form.
/// </summary>
internal static class WorkTaskMapper
{
    /// <summary>The closed set of <see cref="WorkTaskResolutionOutcome"/> values that can round-trip through the wire form.</summary>
    private static readonly IReadOnlyList<WorkTaskResolutionOutcome> outcomes =
        [WorkTaskResolutionOutcome.Succeeded, WorkTaskResolutionOutcome.Waived, WorkTaskResolutionOutcome.Replaced, WorkTaskResolutionOutcome.Failed];

    /// <summary>Domain <see cref="WorkTaskStatus"/> → wire string.</summary>
    public static string ToWire(WorkTaskStatus status)
    {
        return status.Value;
    }

    /// <summary>Wire string → domain <see cref="WorkTaskStatus"/>.</summary>
    public static WorkTaskStatus FromWireStatus(string wire)
    {
        return WorkTaskStatus.All.First(s => string.Equals(s.Value, wire, StringComparison.Ordinal));
    }

    /// <summary>Wire string → domain <see cref="TaskVisibility"/>.</summary>
    public static TaskVisibility FromWireVisibility(string wire)
    {
        return TaskVisibility.FromWire(wire);
    }

    /// <summary>Wire string → domain <see cref="WorkTaskResolutionOutcome"/>; null when absent.</summary>
    public static WorkTaskResolutionOutcome? FromWireOutcome(string? wire)
    {
        return wire is null
            ? null
            : outcomes.First(o => string.Equals(o.Value, wire, StringComparison.Ordinal));
    }

    /// <summary>Aggregate → persistence row (scalar fields only; side collections map side-by-side). <see cref="WorkTaskEntity.Version"/> is the store's optimistic-concurrency counter — not on the domain aggregate.</summary>
    public static WorkTaskEntity ToEntity(WorkTask task, long version)
    {
        return new WorkTaskEntity(
            Id: task.Id.Value,
            ProjectId: task.ProjectId.Value,
            Title: task.Title,
            Brief: task.Brief,
            BriefVersion: task.BriefVersion,
            AttemptOrdinal: task.AttemptOrdinal.Value,
            ActiveAttemptId: task.ActiveAttemptId?.Value,
            Visibility: task.Visibility.Value,
            MissionId: task.MissionId?.Value,
            Status: task.Status.Value,
            ResolutionOutcome: task.ResolutionOutcome?.Value,
            Version: version,
            CreatedAt: task.CreatedAt,
            UpdatedAt: task.UpdatedAt);
    }

    /// <summary>
    /// Persistence row + side collections → aggregate. Calls the
    /// internal rehydrate ctor (covered by <c>InternalsVisibleTo</c>);
    /// the rehydrate path skips every invariant guard because the
    /// persisted state is, by definition, valid.
    /// </summary>
    public static WorkTask ToAggregate(
        WorkTaskEntity entity,
        IEnumerable<WorkTaskSourceRef> sourceRefs,
        IEnumerable<WorkTaskDependency> dependencies)
    {
        var ordinal = new WorkTaskAttemptOrdinal(entity.AttemptOrdinal);
        var outcome = FromWireOutcome(entity.ResolutionOutcome);
        var missionId = entity.MissionId is { } mid ? new MissionId(mid) : (MissionId?)null;
        return new WorkTask(
            id: new WorkTaskId(entity.Id),
            projectId: new ProjectId(entity.ProjectId),
            title: entity.Title,
            brief: entity.Brief,
            briefVersion: entity.BriefVersion,
            attemptOrdinal: ordinal,
            activeAttemptId: entity.ActiveAttemptId is { } rid ? new RunId(rid) : null,
            visibility: FromWireVisibility(entity.Visibility),
            missionId: missionId,
            status: FromWireStatus(entity.Status),
            resolutionOutcome: outcome,
            createdAt: entity.CreatedAt,
            updatedAt: entity.UpdatedAt,
            sourceRefs: sourceRefs,
            dependencies: dependencies);
    }

    /// <summary>Domain <see cref="WorkTaskSourceRef"/> → persistence row (Id round-trips from the aggregate, not a fresh <c>Guid.NewGuid</c>).</summary>
    public static WorkTaskSourceRefEntity ToEntity(WorkTaskSourceRef sourceRef, Guid taskId)
    {
        return new WorkTaskSourceRefEntity(
            sourceRef.Id,
            taskId,
            sourceRef.Kind.Value,
            sourceRef.ExternalId,
            sourceRef.DisplayName,
            sourceRef.IsPrimary,
            sourceRef.LinkNote);
    }

    /// <summary>Persistence row → domain <see cref="WorkTaskSourceRef"/>. Id round-trips back to the aggregate (a rehydrate carries the persisted identity).</summary>
    public static WorkTaskSourceRef ToAggregate(WorkTaskSourceRefEntity entity)
    {
        var rehydrated = WorkTaskSourceRef.Rehydrate(
            id: entity.Id,
            kind: WorkTaskSourceKind.FromWire(entity.Kind),
            externalId: entity.ExternalId,
            displayName: entity.DisplayName,
            isPrimary: entity.IsPrimary,
            linkNote: entity.LinkNote);
        return rehydrated;
    }

    /// <summary>Domain <see cref="WorkTaskDependency"/> → persistence row (Id from the aggregate).</summary>
    public static WorkTaskDependencyEntity ToEntity(WorkTaskDependency dependency, Guid taskId)
    {
        // The cross-mission flag is gated on add-minimal-missions (task 7.6);
        // for now every edge stores cross_mission = false and the read
        // projection emits the same shape for both sides.
        return new WorkTaskDependencyEntity(
            Id: dependency.Id,
            TaskId: taskId,
            PrerequisiteId: dependency.Prerequisite.Value,
            Kind: dependency.Kind.Value,
            CrossMission: false);
    }

    /// <summary>Persistence row → domain <see cref="WorkTaskDependency"/>.</summary>
    public static WorkTaskDependency ToAggregate(WorkTaskDependencyEntity entity)
    {
        return WorkTaskDependency.Rehydrate(
            id: entity.Id,
            dependent: new WorkTaskId(entity.TaskId),
            prerequisite: new WorkTaskId(entity.PrerequisiteId),
            kind: WorkTaskDependencyKind.FromWire(entity.Kind));
    }
}
