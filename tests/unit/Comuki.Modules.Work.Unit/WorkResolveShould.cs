using Comuki.Modules.Work.Domain;
using Comuki.Modules.Work.Domain.Completion;
using Comuki.Modules.Work.Domain.Exceptions;
using Comuki.Modules.Work.Domain.Sources;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Work.Unit;

/// <summary>
/// Aggregate-guard coverage for the Resolve path (tasks 8.1 / 8.2 / 8.3):
/// <see cref="WorkTask.CompletionPolicy"/> required at creation,
/// <see cref="WorkTask.StampLastTerminalAttempt"/> records the
/// authoring actor, and <see cref="WorkTask.EnsureReviewerSeparation"/>
/// rejects same-actor Resolve.
/// </summary>
public sealed class WorkResolveShould
{
    private static readonly ProjectId project = new(Guid.CreateVersion7());

    private static WorkTaskSourceRef PrimarySource()
    {
        return WorkTaskSourceRef.Primary(WorkTaskSourceKind.GitHub, "dot-stbl/comuki#89", "comuki#89");
    }

    [Fact(DisplayName = "Given a Task created without a completion policy, when Create is called with a null policy, then it throws")]
    public void RejectNullCompletionPolicy()
    {
        Should.Throw<WorkTaskDomainException>(
            static () => WorkTask.Create(project, "t", "b", PrimarySource(), null!, DateTimeOffset.UtcNow));
    }

    [Fact(DisplayName = "Given a Task created with an empty contract, when Create is called, then it throws")]
    public void RejectEmptyCompletionContract()
    {
        var emptyContract = new EvidenceContract(new HashSet<EvidenceKind>());
        Should.Throw<WorkTaskDomainException>(
            () => WorkTask.Create(
                project,
                "t",
                "b",
                PrimarySource(),
                new WorkTaskCompletionPolicy(CompletionPolicyKind.Deterministic, emptyContract, Version: 1),
                DateTimeOffset.UtcNow));
    }

    [Fact(DisplayName = "Given a fresh Task, when stamp terminal attempt fires, then EnsureReviewerSeparation accepts a distinct actor")]
    public void ReviewerSeparationAcceptsDistinctActor()
    {
        var now = DateTimeOffset.UtcNow;
        var task = WorkTask.Create(
            project,
            "t",
            "b",
            PrimarySource(),
            WorkTaskCompletionPolicy.Default(now),
            now);

        task.StampLastTerminalAttempt("actor-A", now);

        // Should not throw — distinct human reviewer.
        task.EnsureReviewerSeparation("actor-B");
    }

    [Fact(DisplayName = "Given a Task with a stamped terminal attempt, when EnsureReviewerSeparation is called with the same actor, then it throws")]
    public void ReviewerSeparationRejectsSameActor()
    {
        var now = DateTimeOffset.UtcNow;
        var task = WorkTask.Create(
            project,
            "t",
            "b",
            PrimarySource(),
            WorkTaskCompletionPolicy.Default(now),
            now);

        task.StampLastTerminalAttempt("actor-A", now);

        Should.Throw<WorkTaskDomainException>(
            () => task.EnsureReviewerSeparation("actor-A"));
    }

    [Fact(DisplayName = "Given a fresh Task with no terminal attempt, when EnsureReviewerSeparation is called, then it throws")]
    public void ReviewerSeparationRejectsWithoutTerminal()
    {
        var now = DateTimeOffset.UtcNow;
        var task = WorkTask.Create(
            project,
            "t",
            "b",
            PrimarySource(),
            WorkTaskCompletionPolicy.Default(now),
            now);

        Should.Throw<WorkTaskDomainException>(
            () => task.EnsureReviewerSeparation("actor-A"));
    }
}
