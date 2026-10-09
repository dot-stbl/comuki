using Comuki.Modules.Work.Domain.Exceptions;
using Comuki.Modules.Work.Domain.Ids;

namespace Comuki.Modules.Work.Domain.Dependencies;

/// <summary>
/// One edge of the Task-to-Task graph. A Task's dependency set is
/// the union of all edges where it is the dependent side; the
/// read projection is what callers see. The <c>redacted</c> flag is
/// the runtime hint the projection reads to decide whether to emit
/// a redacted external-dependency stub for cross-Mission viewers
/// (the cross-Mission read-side logic itself lands in task 7.6; the
/// flag is set at the moment an edge crosses Missions — which is
/// also part of 7.6 and intentionally not set by the
/// non-cross-Mission edge factory below).
/// </summary>
public sealed class WorkTaskDependency
{
    private WorkTaskDependency(
        WorkTaskId dependent,
        WorkTaskId prerequisite,
        WorkTaskDependencyKind kind)
        : this(id: Guid.CreateVersion7(), dependent: dependent, prerequisite: prerequisite, kind: kind)
    {
    }

    private WorkTaskDependency(
        Guid id,
        WorkTaskId dependent,
        WorkTaskId prerequisite,
        WorkTaskDependencyKind kind)
    {
        Id = id;
        Dependent = dependent;
        Prerequisite = prerequisite;
        Kind = kind;
    }

    /// <summary>Stable identifier (round-trips through EF; the <see cref="Create"/> factory mints a new one).</summary>
    public Guid Id { get; }

    /// <summary>The Task that owns the edge (cannot resolve without the prerequisite).</summary>
    public WorkTaskId Dependent { get; }

    /// <summary>The Task the edge points to.</summary>
    public WorkTaskId Prerequisite { get; }

    /// <summary>The shape of the edge (<see cref="WorkTaskDependencyKind.Blocks"/> or <see cref="WorkTaskDependencyKind.RelatesTo"/>).</summary>
    public WorkTaskDependencyKind Kind { get; }

    /// <summary>
    /// Creates a dependency edge. Self-references are rejected —
    /// a Task does not depend on itself. The kind must be
    /// non-<see cref="WorkTaskDependencyKind.Unspecified"/>; the
    /// caller chooses whether the edge is informational
    /// (<see cref="WorkTaskDependencyKind.RelatesTo"/>) or
    /// scheduling-affecting
    /// (<see cref="WorkTaskDependencyKind.Blocks"/>).
    /// </summary>
    /// <exception cref="WorkTaskDomainException">A factory invariant was violated.</exception>
    public static WorkTaskDependency Create(
        WorkTaskId dependent,
        WorkTaskId prerequisite,
        WorkTaskDependencyKind kind)
    {
        return dependent == prerequisite
            ? throw new WorkTaskDomainException(
                WorkTaskErrorCodes.TitleEmpty,
                "a task cannot depend on itself")
            : kind == WorkTaskDependencyKind.Unspecified
            ? throw new WorkTaskDomainException(
                WorkTaskErrorCodes.TitleEmpty,
                "dependency kind must be Blocks or RelatesTo")
            : new WorkTaskDependency(dependent, prerequisite, kind);
    }

    /// <summary>
    /// EF-rehydrate factory — carries the persisted <see cref="Id"/>
    /// from the storage row into the aggregate so a re-read sees
    /// the same edge identity it had on the prior save.
    /// </summary>
    internal static WorkTaskDependency Rehydrate(
        Guid id,
        WorkTaskId dependent,
        WorkTaskId prerequisite,
        WorkTaskDependencyKind kind)
    {
        return new WorkTaskDependency(id, dependent, prerequisite, kind);
    }
}
