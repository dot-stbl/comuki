using Comuki.Modules.Work.Domain;
using Comuki.Modules.Work.Domain.Attempts;
using Comuki.Modules.Work.Domain.Completion;
using Comuki.Modules.Work.Domain.Dependencies;
using Comuki.Modules.Work.Domain.Exceptions;
using Comuki.Modules.Work.Domain.Ids;
using Comuki.Modules.Work.Domain.Sources;
using Comuki.Modules.Work.Domain.Visibility;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Work.Unit;

/// <summary>
/// Aggregate-guard coverage for <see cref="WorkTask"/>: factory,
/// legal/illegal status transitions, one-active-Run invariant,
/// mission-attachment one-way guard, and resolution-outcome
/// immutability. The full transition matrix is asserted in
/// <see cref="WorkTaskTransitionsShould"/>; the aggregate tests
/// exercise the guards that close the matrix on the entity side.
/// </summary>
public sealed class WorkTaskShould
{
    private const string Title = "Wire the inbound webhook to WorkTask";
    private const string Brief = "Admit a tracker ticket and dispatch a Run.";

    private static readonly ProjectId project = new(Guid.CreateVersion7());
    private static readonly MissionId mission = new(Guid.CreateVersion7());

    private static WorkTaskSourceRef PrimarySource()
    {
        return WorkTaskSourceRef.Primary(WorkTaskSourceKind.GitHub, "dot-stbl/comuki#89", "comuki#89");
    }

    private static WorkTask NewDraft(DateTimeOffset now)
    {
        return WorkTask.Create(project, Title, Brief, PrimarySource(), WorkTaskCompletionPolicy.Default(now), now);
    }

    [Fact(DisplayName = "Given factory inputs, when Create is called, then the Task is Draft with no attempts and a primary source")]
    public void CreateDraftTask()
    {
        var now = DateTimeOffset.UtcNow;

        var task = WorkTask.Create(project, Title, Brief, PrimarySource(), WorkTaskCompletionPolicy.Default(now), now);

        task.Id.Value.Version.ShouldBe(7);
        task.ProjectId.ShouldBe(project);
        task.Title.ShouldBe(Title);
        task.Brief.ShouldBe(Brief);
        task.BriefVersion.ShouldBe(1);
        task.Status.ShouldBe(WorkTaskStatus.Draft);
        task.AttemptOrdinal.ShouldBe(WorkTaskAttemptOrdinal.None);
        task.ActiveAttemptId.ShouldBeNull();
        task.HasActiveAttempt.ShouldBeFalse();
        task.Visibility.ShouldBe(TaskVisibility.Project);
        task.MissionId.ShouldBeNull();
        task.ResolutionOutcome.ShouldBeNull();
        task.SourceRefs.Count.ShouldBe(1);
        task.SourceRefs[0].IsPrimary.ShouldBeTrue();
        task.Dependencies.Count.ShouldBe(0);
        task.CreatedAt.ShouldBe(now);
        task.UpdatedAt.ShouldBe(now);
    }

    [Fact(DisplayName = "Given an empty guid project id, when Create is called, then it throws")]
    public void RejectEmptyProjectId()
    {
        Should.Throw<WorkTaskDomainException>(
            static () => WorkTask.Create(new ProjectId(Guid.Empty), Title, Brief, PrimarySource(), WorkTaskCompletionPolicy.Default(DateTimeOffset.UtcNow), DateTimeOffset.UtcNow));
    }

    [Fact(DisplayName = "Given an empty title, when Create is called, then it throws")]
    public void RejectEmptyTitle()
    {
        Should.Throw<WorkTaskDomainException>(
            static () => WorkTask.Create(project, " ", Brief, PrimarySource(), WorkTaskCompletionPolicy.Default(DateTimeOffset.UtcNow), DateTimeOffset.UtcNow));
    }

    [Fact(DisplayName = "Given an empty brief, when Create is called, then it throws")]
    public void RejectEmptyBrief()
    {
        Should.Throw<WorkTaskDomainException>(
            static () => WorkTask.Create(project, Title, " ", PrimarySource(), WorkTaskCompletionPolicy.Default(DateTimeOffset.UtcNow), DateTimeOffset.UtcNow));
    }

    [Fact(DisplayName = "Given a non-primary bootstrap source, when Create is called, then it throws")]
    public void RejectNonPrimaryBootstrapSource()
    {
        var nonPrimary = WorkTaskSourceRef.Related(WorkTaskSourceKind.GitHub, "dot-stbl/comuki#89", "comuki#89");

        Should.Throw<WorkTaskDomainException>(
            () => WorkTask.Create(project, Title, Brief, nonPrimary, WorkTaskCompletionPolicy.Default(DateTimeOffset.UtcNow), DateTimeOffset.UtcNow));
    }

    [Fact(DisplayName = "Given a primary source, when AddSourceRef(primary) is called, then it throws because primary is fixed at create")]
    public void RejectAddingPrimary()
    {
        var task = NewDraft(DateTimeOffset.UtcNow);

        var anotherPrimary = WorkTaskSourceRef.Primary(WorkTaskSourceKind.GitLab, "dot-stbl/comuki#1", "comuki#1");

        Should.Throw<WorkTaskDomainException>(
            () => task.AddSourceRef(anotherPrimary, DateTimeOffset.UtcNow));
    }

    [Fact(DisplayName = "Given a related source for a fresh kind/id, when AddSourceRef is called, then the source is appended")]
    public void AppendRelatedSource()
    {
        var task = NewDraft(DateTimeOffset.UtcNow);
        var related = WorkTaskSourceRef.Related(WorkTaskSourceKind.Jira, "COMUKI-89", "comuki-89");

        task.AddSourceRef(related, DateTimeOffset.UtcNow);

        task.SourceRefs.Count.ShouldBe(2);
        task.SourceRefs.ShouldContain(related);
    }

    [Fact(DisplayName = "Given a duplicate related source, when AddSourceRef is called, then it throws")]
    public void RejectDuplicateRelatedSource()
    {
        var task = NewDraft(DateTimeOffset.UtcNow);
        task.AddSourceRef(
            WorkTaskSourceRef.Related(WorkTaskSourceKind.Jira, "COMUKI-89", "comuki-89"),
            DateTimeOffset.UtcNow);

        Should.Throw<WorkTaskDomainException>(
            () => task.AddSourceRef(
                WorkTaskSourceRef.Related(WorkTaskSourceKind.Jira, "COMUKI-89", "comuki-89-other"),
                DateTimeOffset.UtcNow));
    }

    [Fact(DisplayName = "Given a non-terminal status, when ReviseBrief is called, then the brief is updated and the version is bumped")]
    public void ReviseBriefBumpsVersion()
    {
        var task = NewDraft(DateTimeOffset.UtcNow);

        task.ReviseBrief("Admit a tracker ticket, dispatch a Run, and emit a status event.", DateTimeOffset.UtcNow);

        task.BriefVersion.ShouldBe(2);
        task.Brief.ShouldBe("Admit a tracker ticket, dispatch a Run, and emit a status event.");
    }

    [Fact(DisplayName = "Given a terminal status, when ReviseBrief is called, then it throws")]
    public void RejectReviseBriefOnTerminal()
    {
        var task = NewDraft(DateTimeOffset.UtcNow);
        task.TransitionTo(WorkTaskStatus.Ready, null, DateTimeOffset.UtcNow);
        task.TransitionTo(WorkTaskStatus.Active, null, DateTimeOffset.UtcNow);
        task.TransitionTo(WorkTaskStatus.Resolved, WorkTaskResolutionOutcome.Succeeded, DateTimeOffset.UtcNow);

        Should.Throw<WorkTaskDomainException>(
            () => task.ReviseBrief("a new brief", DateTimeOffset.UtcNow));
    }

    [Fact(DisplayName = "Given a Draft task, when TransitionTo Ready is called, then the status changes")]
    public void LegalTransitionDraftToReady()
    {
        var task = NewDraft(DateTimeOffset.UtcNow);
        var now = DateTimeOffset.UtcNow;

        task.TransitionTo(WorkTaskStatus.Ready, null, now);

        task.Status.ShouldBe(WorkTaskStatus.Ready);
    }

    [Fact(DisplayName = "Given a Draft task, when TransitionTo Resolved is called without an outcome, then it throws")]
    public void RejectResolvedWithoutOutcome()
    {
        var task = NewDraft(DateTimeOffset.UtcNow);

        Should.Throw<WorkTaskDomainException>(
            () => task.TransitionTo(WorkTaskStatus.Resolved, null, DateTimeOffset.UtcNow));
    }

    [Fact(DisplayName = "Given a non-terminal status, when TransitionTo Resolved with an outcome is called, then the outcome is set")]
    public void LegalTransitionToResolvedSetsOutcome()
    {
        var task = NewDraft(DateTimeOffset.UtcNow);
        task.TransitionTo(WorkTaskStatus.Ready, null, DateTimeOffset.UtcNow);
        task.TransitionTo(WorkTaskStatus.Active, null, DateTimeOffset.UtcNow);

        task.TransitionTo(WorkTaskStatus.Resolved, WorkTaskResolutionOutcome.Succeeded, DateTimeOffset.UtcNow);

        task.Status.ShouldBe(WorkTaskStatus.Resolved);
        task.ResolutionOutcome.ShouldBe(WorkTaskResolutionOutcome.Succeeded);
    }

    [Fact(DisplayName = "Given a Resolved task, when TransitionTo Resolved with a second outcome is called, then it throws")]
    public void RejectSecondResolution()
    {
        var task = NewDraft(DateTimeOffset.UtcNow);
        task.TransitionTo(WorkTaskStatus.Ready, null, DateTimeOffset.UtcNow);
        task.TransitionTo(WorkTaskStatus.Active, null, DateTimeOffset.UtcNow);
        task.TransitionTo(WorkTaskStatus.Resolved, WorkTaskResolutionOutcome.Succeeded, DateTimeOffset.UtcNow);

        Should.Throw<WorkTaskDomainException>(
            () => task.TransitionTo(WorkTaskStatus.Resolved, WorkTaskResolutionOutcome.Waived, DateTimeOffset.UtcNow));
    }

    [Fact(DisplayName = "Given a non-Resolved transition, when an outcome is supplied, then it throws")]
    public void RejectOutcomeOnNonResolvedTransition()
    {
        var task = NewDraft(DateTimeOffset.UtcNow);

        Should.Throw<WorkTaskDomainException>(
            () => task.TransitionTo(WorkTaskStatus.Ready, WorkTaskResolutionOutcome.Waived, DateTimeOffset.UtcNow));
    }

    [Fact(DisplayName = "Given an illegal transition, when TransitionTo is called, then it throws")]
    public void RejectIllegalTransition()
    {
        var task = NewDraft(DateTimeOffset.UtcNow);

        Should.Throw<WorkTaskDomainException>(
            () => task.TransitionTo(WorkTaskStatus.Resolved, WorkTaskResolutionOutcome.Succeeded, DateTimeOffset.UtcNow));
    }

    [Fact(DisplayName = "Given a Draft task, when AppendAttempt is called, then it throws because Ready / Blocked is required")]
    public void RejectAppendAttemptFromDraft()
    {
        var task = NewDraft(DateTimeOffset.UtcNow);

        Should.Throw<WorkTaskDomainException>(
            () => task.AppendAttempt(RunId.New(), DateTimeOffset.UtcNow));
    }

    [Fact(DisplayName = "Given a Ready task with no active attempt, when AppendAttempt is called, then the ordinal becomes 1 and the run is recorded")]
    public void AppendFirstAttempt()
    {
        var task = NewDraft(DateTimeOffset.UtcNow);
        task.TransitionTo(WorkTaskStatus.Ready, null, DateTimeOffset.UtcNow);

        var runId = RunId.New();
        var ordinal = task.AppendAttempt(runId, DateTimeOffset.UtcNow);

        ordinal.Value.ShouldBe(1);
        task.AttemptOrdinal.Value.ShouldBe(1);
        task.ActiveAttemptId.ShouldBe(runId);
        task.HasActiveAttempt.ShouldBeTrue();
    }

    [Fact(DisplayName = "Given an active attempt, when a second AppendAttempt with a different run id is called, then it throws")]
    public void RejectConcurrentAppendAttempt()
    {
        var task = NewDraft(DateTimeOffset.UtcNow);
        task.TransitionTo(WorkTaskStatus.Ready, null, DateTimeOffset.UtcNow);
        task.AppendAttempt(RunId.New(), DateTimeOffset.UtcNow);

        Should.Throw<WorkTaskDomainException>(
            () => task.AppendAttempt(RunId.New(), DateTimeOffset.UtcNow));
    }

    [Fact(DisplayName = "Given an active attempt, when AppendAttempt with the same run id is called, then the ordinal is unchanged")]
    public void AppendAttemptIsIdempotentOnSameRunId()
    {
        var task = NewDraft(DateTimeOffset.UtcNow);
        task.TransitionTo(WorkTaskStatus.Ready, null, DateTimeOffset.UtcNow);
        var runId = RunId.New();
        task.AppendAttempt(runId, DateTimeOffset.UtcNow);

        var ordinal = task.AppendAttempt(runId, DateTimeOffset.UtcNow);

        ordinal.Value.ShouldBe(1);
        task.AttemptOrdinal.Value.ShouldBe(1);
    }

    [Fact(DisplayName = "Given a Blocked task with a completed attempt, when AppendAttempt is called, then a new attempt ordinal is appended")]
    public void AppendAfterCompletion()
    {
        var task = NewDraft(DateTimeOffset.UtcNow);
        task.TransitionTo(WorkTaskStatus.Ready, null, DateTimeOffset.UtcNow);
        var firstRun = RunId.New();
        task.AppendAttempt(firstRun, DateTimeOffset.UtcNow);
        task.CompleteAttempt(firstRun, DateTimeOffset.UtcNow);
        task.TransitionTo(WorkTaskStatus.Blocked, null, DateTimeOffset.UtcNow);

        var secondOrdinal = task.AppendAttempt(RunId.New(), DateTimeOffset.UtcNow);

        secondOrdinal.Value.ShouldBe(2);
        task.ActiveAttemptId.ShouldNotBeNull();
        task.ActiveAttemptId.ShouldNotBe(firstRun);
    }

    [Fact(DisplayName = "Given no active attempt, when CompleteAttempt is called, then it throws")]
    public void RejectCompleteAttemptWithNoActive()
    {
        var task = NewDraft(DateTimeOffset.UtcNow);

        Should.Throw<WorkTaskDomainException>(
            () => task.CompleteAttempt(RunId.New(), DateTimeOffset.UtcNow));
    }

    [Fact(DisplayName = "Given a different run id, when CompleteAttempt is called, then it throws")]
    public void RejectCompleteAttemptWithMismatchedRunId()
    {
        var task = NewDraft(DateTimeOffset.UtcNow);
        task.TransitionTo(WorkTaskStatus.Ready, null, DateTimeOffset.UtcNow);
        var runId = RunId.New();
        task.AppendAttempt(runId, DateTimeOffset.UtcNow);

        Should.Throw<WorkTaskDomainException>(
            () => task.CompleteAttempt(RunId.New(), DateTimeOffset.UtcNow));
    }

    [Fact(DisplayName = "Given a standalone Task, when AttachToMission is called, then the visibility flips to mission")]
    public void AttachToMissionOneWay()
    {
        var task = NewDraft(DateTimeOffset.UtcNow);

        task.AttachToMission(mission, DateTimeOffset.UtcNow);

        task.Visibility.ShouldBe(TaskVisibility.Mission);
        task.MissionId.ShouldBe(mission);
    }

    [Fact(DisplayName = "Given a mission-attached Task, when AttachToMission is called again, then it throws")]
    public void RejectSecondMissionAttachment()
    {
        var task = NewDraft(DateTimeOffset.UtcNow);
        task.AttachToMission(mission, DateTimeOffset.UtcNow);

        Should.Throw<WorkTaskDomainException>(
            () => task.AttachToMission(new MissionId(Guid.CreateVersion7()), DateTimeOffset.UtcNow));
    }

    [Fact(DisplayName = "Given a Task, when AddDependency with the task's own id is called, then it throws")]
    public void RejectSelfDependency()
    {
        var task = NewDraft(DateTimeOffset.UtcNow);

        Should.Throw<WorkTaskDomainException>(
            () => task.AddDependency(task.Id, WorkTaskDependencyKind.Blocks, DateTimeOffset.UtcNow));
    }

    [Fact(DisplayName = "Given a Task, when AddDependency with a fresh prerequisite is called, then the edge is appended")]
    public void AppendDependency()
    {
        var task = NewDraft(DateTimeOffset.UtcNow);
        var otherTask = new WorkTaskId(Guid.CreateVersion7());

        task.AddDependency(otherTask, WorkTaskDependencyKind.Blocks, DateTimeOffset.UtcNow);

        task.Dependencies.Count.ShouldBe(1);
        task.Dependencies[0].Prerequisite.ShouldBe(otherTask);
        task.Dependencies[0].Kind.ShouldBe(WorkTaskDependencyKind.Blocks);
    }

    [Fact(DisplayName = "Given a Task with a Blocks edge, when AddDependency with the same prerequisite and kind is called, then it throws")]
    public void RejectDuplicateDependency()
    {
        var task = NewDraft(DateTimeOffset.UtcNow);
        var otherTask = new WorkTaskId(Guid.CreateVersion7());
        task.AddDependency(otherTask, WorkTaskDependencyKind.Blocks, DateTimeOffset.UtcNow);

        Should.Throw<WorkTaskDomainException>(
            () => task.AddDependency(otherTask, WorkTaskDependencyKind.Blocks, DateTimeOffset.UtcNow));
    }
}
