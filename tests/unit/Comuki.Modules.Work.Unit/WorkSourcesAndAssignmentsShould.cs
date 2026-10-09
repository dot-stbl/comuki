using Comuki.Modules.Work.Domain;
using Comuki.Modules.Work.Domain.Assignments;
using Comuki.Modules.Work.Domain.Completion;
using Comuki.Modules.Work.Domain.Exceptions;
using Comuki.Modules.Work.Domain.Sources;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Work.Unit;

/// <summary>
/// Aggregate-guard coverage for the multi-source + primary-change + assignment
/// paths (tasks 7.1 / 7.2 / 7.3 / 7.5): a Task may aggregate multiple
/// sources with exactly one primary; <see cref="WorkTask.ChangePrimarySource"/>
/// promotes an existing related source to primary and demotes the
/// previous primary with a link note; <see cref="WorkTask.AddAssignment"/>
/// rejects duplicates and self-approval is rejected in
/// <see cref="WorkTask.ApproveAssignment"/>.
/// </summary>
public sealed class WorkSourcesAndAssignmentsShould
{
    private static readonly ProjectId project = new(Guid.CreateVersion7());

    private static WorkTaskSourceRef PrimarySource()
    {
        return WorkTaskSourceRef.Primary(WorkTaskSourceKind.GitHub, "dot-stbl/comuki#89", "comuki#89");
    }

    private static WorkTask NewTask(DateTimeOffset now)
    {
        return WorkTask.Create(project, "t", "b", PrimarySource(), WorkTaskCompletionPolicy.Default(now), now);
    }

    [Fact(DisplayName = "Given a Task with one primary and one related source, when ChangePrimarySource fires with the related id, then the related becomes primary and the old primary becomes related with a link note")]
    public void ChangePrimarySourceSwapsAndAnnotates()
    {
        var now = DateTimeOffset.UtcNow;
        var task = NewTask(now);
        var related = WorkTaskSourceRef.Related(WorkTaskSourceKind.Jira, "COM-89", "comuki-89");
        task.AddSourceRef(related, now);

        // The change demotes the existing primary and promotes the
        // related source — both copies carry fresh ids (the demote /
        // promote factories allocate). The captured relatedId from
        // before the change no longer matches the current primary.
        task.ChangePrimarySource(related.Id, now);

        var currentPrimary = task.SourceRefs.Single(static source => source.IsPrimary);
        // The new primary carries the Jira kind / external id we added.
        currentPrimary.Kind.ShouldBe(WorkTaskSourceKind.Jira);
        currentPrimary.ExternalId.ShouldBe("COM-89");
        // The former primary is now related with a link note.
        var formerPrimary = task.SourceRefs.Single(static source => source.Kind == WorkTaskSourceKind.GitHub);
        formerPrimary.IsPrimary.ShouldBeFalse();
        formerPrimary.LinkNote.ShouldNotBeNull();
        formerPrimary.LinkNote!.ShouldContain("ChangePrimarySourceDecision", Case.Insensitive);
    }

    [Fact(DisplayName = "Given a Task with one primary, when ChangePrimarySource fires with an unknown id, then it throws")]
    public void ChangePrimarySourceUnknownIdThrows()
    {
        var now = DateTimeOffset.UtcNow;
        var task = NewTask(now);

        Should.Throw<WorkTaskDomainException>(
            () => task.ChangePrimarySource(Guid.NewGuid(), now));
    }

    [Fact(DisplayName = "Given a Task with a primary already, when AddAssignment fires for the same (kind, actor) pair, then it throws")]
    public void AddAssignmentRejectsDuplicate()
    {
        var now = DateTimeOffset.UtcNow;
        var task = NewTask(now);

        task.AddAssignment(new WorkTaskAssignment(
            Id: Guid.CreateVersion7(),
            ActorKind: AssignmentActorKind.Service,
            ActorId: "brain-svc",
            CapacityHint: 5,
            ProposalRequired: false,
            ProposalState: AssignmentProposalState.Direct,
            ProposedBy: null,
            AssignedAt: now));

        Should.Throw<WorkTaskDomainException>(
            () => task.AddAssignment(new WorkTaskAssignment(
                Id: Guid.CreateVersion7(),
                ActorKind: AssignmentActorKind.Service,
                ActorId: "brain-svc",
                CapacityHint: 5,
                ProposalRequired: false,
                ProposalState: AssignmentProposalState.Direct,
                ProposedBy: null,
                AssignedAt: now)));
    }

    [Fact(DisplayName = "Given a Human-proposed assignment, when the same actor approves it, then it throws (no self-approval)")]
    public void ApproveAssignmentRejectsSelfApproval()
    {
        var now = DateTimeOffset.UtcNow;
        var task = NewTask(now);
        var assignmentId = Guid.CreateVersion7();
        task.AddAssignment(new WorkTaskAssignment(
            Id: assignmentId,
            ActorKind: AssignmentActorKind.Human,
            ActorId: "human-A",
            CapacityHint: null,
            ProposalRequired: true,
            ProposalState: AssignmentProposalState.Proposed,
            ProposedBy: "human-A",
            AssignedAt: now));

        Should.Throw<WorkTaskDomainException>(
            () => task.ApproveAssignment(assignmentId, "human-A", now));
    }

    [Fact(DisplayName = "Given a Human-proposed assignment, when a distinct human approves it, then the proposal state moves to Approved")]
    public void ApproveAssignmentAcceptsDistinctApprover()
    {
        var now = DateTimeOffset.UtcNow;
        var task = NewTask(now);
        var assignmentId = Guid.CreateVersion7();
        task.AddAssignment(new WorkTaskAssignment(
            Id: assignmentId,
            ActorKind: AssignmentActorKind.Human,
            ActorId: "human-A",
            CapacityHint: null,
            ProposalRequired: true,
            ProposalState: AssignmentProposalState.Proposed,
            ProposedBy: "human-A",
            AssignedAt: now));

        task.ApproveAssignment(assignmentId, "human-B", now);

        task.Assignments.Single(assignment => assignment.Id == assignmentId)
            .ProposalState.ShouldBe(AssignmentProposalState.Approved);
    }
}
