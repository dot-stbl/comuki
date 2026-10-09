namespace Comuki.Modules.Work.Domain.Assignments;

/// <summary>
/// Kind of actor a <see cref="WorkTaskAssignment"/> represents — the
/// umbrella's task 7.3 axes. The kind decides whether
/// <see cref="Decisions.DecisionProvenance"/>
/// gates the assignment on a proposal + approval (Human) or allows
/// direct auto-assignment (Service under capacity).
/// </summary>
public enum AssignmentActorKind
{
    /// <summary>A human operator — proposals must be approved by a distinct human.</summary>
    Human = 1,

    /// <summary>A Brain / automation service — auto-assignment under the configured capacity.</summary>
    Service = 2,
}
