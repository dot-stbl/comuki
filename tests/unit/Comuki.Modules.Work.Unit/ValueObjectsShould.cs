using Comuki.Modules.Work.Domain;
using Comuki.Modules.Work.Domain.Attempts;
using Comuki.Modules.Work.Domain.Dependencies;
using Comuki.Modules.Work.Domain.Exceptions;
using Comuki.Modules.Work.Domain.Ids;
using Comuki.Modules.Work.Domain.Sources;
using Comuki.Modules.Work.Domain.Visibility;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Work.Unit;

/// <summary>
/// Tests for the WorkTask value objects. Domain invariants only — no
/// EF / persistence / serialization in scope (lives in
/// Work.Infrastructure / tasks 3.1–3.7).
/// </summary>
public sealed class ValueObjectsShould
{
    [Fact(DisplayName = "Given WorkTaskId.New, when constructed, then the value is a UUIDv7")]
    public void WorkTaskIdIsV7()
    {
        var id = WorkTaskId.New();

        id.Value.Version.ShouldBe(7);
    }

    [Fact(DisplayName = "Given WorkTaskAttemptOrdinal.None, then the value is zero and HasAttempt is false")]
    public void AttemptOrdinalNoneIsZero()
    {
        var none = WorkTaskAttemptOrdinal.None;

        none.Value.ShouldBe(0);
        none.IsNone.ShouldBeTrue();
        none.HasAttempt.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given WorkTaskAttemptOrdinal.None, when Next is called, then the result is 1")]
    public void NextFromNoneIsOne()
    {
        var next = WorkTaskAttemptOrdinal.Next(WorkTaskAttemptOrdinal.None);

        next.Value.ShouldBe(1);
        next.HasAttempt.ShouldBeTrue();
    }

    [Fact(DisplayName = "Given WorkTaskAttemptOrdinal(5), when Next is called, then the result is 6")]
    public void NextMonotonic()
    {
        var next = WorkTaskAttemptOrdinal.Next(new WorkTaskAttemptOrdinal(5));

        next.Value.ShouldBe(6);
    }

    [Fact(DisplayName = "Given a self-dependency, when WorkTaskDependency.Create is called, then it throws")]
    public void RejectSelfDependency()
    {
        var id = WorkTaskId.New();

        Should.Throw<WorkTaskDomainException>(
            () => WorkTaskDependency.Create(id, id, WorkTaskDependencyKind.Blocks));
    }

    [Fact(DisplayName = "Given an Unspecified kind, when WorkTaskDependency.Create is called, then it throws")]
    public void RejectUnspecifiedKind()
    {
        var dependent = WorkTaskId.New();
        var prerequisite = WorkTaskId.New();

        Should.Throw<WorkTaskDomainException>(
            () => WorkTaskDependency.Create(dependent, prerequisite, WorkTaskDependencyKind.Unspecified));
    }

    [Fact(DisplayName = "Given two distinct ids, when WorkTaskDependency.Create is called, then the edge carries both ends and the kind")]
    public void CreateDependencyEdge()
    {
        var dependent = WorkTaskId.New();
        var prerequisite = WorkTaskId.New();

        var edge = WorkTaskDependency.Create(dependent, prerequisite, WorkTaskDependencyKind.RelatesTo);

        edge.Dependent.ShouldBe(dependent);
        edge.Prerequisite.ShouldBe(prerequisite);
        edge.Kind.ShouldBe(WorkTaskDependencyKind.RelatesTo);
    }

    [Fact(DisplayName = "Given a Primary factory call, then the result is primary")]
    public void PrimarySourceIsPrimary()
    {
        var source = WorkTaskSourceRef.Primary(WorkTaskSourceKind.GitHub, "owner/repo#1", "title");

        source.IsPrimary.ShouldBeTrue();
        source.Kind.ShouldBe(WorkTaskSourceKind.GitHub);
        source.ExternalId.ShouldBe("owner/repo#1");
    }

    [Fact(DisplayName = "Given an empty external id, when WorkTaskSourceRef.Primary is called, then it throws")]
    public void RejectEmptyExternalId()
    {
        Should.Throw<WorkTaskDomainException>(
            static () => WorkTaskSourceRef.Primary(WorkTaskSourceKind.GitHub, " ", "title"));
    }

    [Fact(DisplayName = "Given an empty display name, when WorkTaskSourceRef.Related is called, then it throws")]
    public void RejectEmptyDisplayName()
    {
        Should.Throw<WorkTaskDomainException>(
            static () => WorkTaskSourceRef.Related(WorkTaskSourceKind.GitHub, "owner/repo#1", " "));
    }

    [Fact(DisplayName = "Given TaskVisibility from wire \"Mission\", when FromWire is called, then the result is Mission")]
    public void ParseVisibilityFromWire()
    {
        TaskVisibility.FromWire("Mission").ShouldBe(TaskVisibility.Mission);
    }

    [Fact(DisplayName = "Given an unknown wire value, when TaskVisibility.FromWire is called, then it throws")]
    public void RejectUnknownVisibilityWire()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            static () => TaskVisibility.FromWire("Limbo"));
    }

    [Fact(DisplayName = "Given WorkTaskStatus.All, when enumerated, then the working statuses are returned in declaration order")]
    public void WorkTaskStatusAllHasEveryWorkingValue()
    {
        WorkTaskStatus.All.ShouldBe(
            [
                WorkTaskStatus.Draft,
                WorkTaskStatus.Ready,
                WorkTaskStatus.Active,
                WorkTaskStatus.Blocked,
                WorkTaskStatus.Resolved,
                WorkTaskStatus.Cancelled,
            ],
            ignoreOrder: false);
    }

    [Fact(DisplayName = "Given a non-terminal status, when Cancellable is checked, then it is in the set")]
    public void CancellableStatuses()
    {
        WorkTaskStatus.Cancellable.ShouldContain(WorkTaskStatus.Draft);
        WorkTaskStatus.Cancellable.ShouldContain(WorkTaskStatus.Ready);
        WorkTaskStatus.Cancellable.ShouldContain(WorkTaskStatus.Active);
        WorkTaskStatus.Cancellable.ShouldContain(WorkTaskStatus.Blocked);
        WorkTaskStatus.Cancellable.ShouldNotContain(WorkTaskStatus.Resolved);
        WorkTaskStatus.Cancellable.ShouldNotContain(WorkTaskStatus.Cancelled);
    }
}
