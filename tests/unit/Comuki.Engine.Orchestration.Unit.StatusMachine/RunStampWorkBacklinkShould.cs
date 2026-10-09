using Comuki.Engine.Orchestration.Domain.Exceptions;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Orchestration.Unit.StatusMachine;

/// <summary>
/// Tests for the Work-side backlink on <see cref="Run"/> — the
/// <see cref="Run.StampWorkBacklink"/> mutator the Work bridge calls
/// when it claims a Run for a <c>(WorkTask, attempt)</c> pair. The
/// stamp is the engine's record of "one WorkTask owns one Run" — a
/// second stamp with a different pair is a programming error on the
/// bridge side, not a recoverable condition, so the rejection throws
/// a typed <see cref="OrchestrationDomainException"/> with the
/// stable <see cref="OrchestrationErrorCodes.RunWorkBacklinkMismatch"/>
/// code (clients branch on <c>Code</c>, the audit log on the
/// message).
/// </summary>
public sealed class RunStampWorkBacklinkShould
{
    [Fact(DisplayName = "Given an unstamped run, when StampWorkBacklink is called, then the four backlink fields are populated and updated_at advances to the stamp time")]
    public void InitialStampPopulatesBacklink()
    {
        var createdAt = DateTimeOffset.UtcNow;
        var run = Run.Create(ProjectId.New(), createdAt);

        var taskId = Guid.CreateVersion7();
        var predecessor = Guid.CreateVersion7();
        var stampAt = createdAt.AddSeconds(1);

        run.StampWorkBacklink(
            taskId,
            ordinal: 1,
            predecessorRunId: predecessor,
            triggeringActorId: "integrations/github-bot",
            now: stampAt);

        run.TaskId.ShouldBe(taskId);
        run.AttemptOrdinal.ShouldBe(1);
        run.PredecessorRunId.ShouldBe(predecessor);
        run.TriggeringActorId.ShouldBe("integrations/github-bot");
        run.UpdatedAt.ShouldBe(stampAt);
    }

    [Fact(DisplayName = "Given an already-stamped run, when StampWorkBacklink is called with the same (taskId, ordinal) pair, then the fields and updated_at are unchanged")]
    public void SameStampIsNoOp()
    {
        var createdAt = DateTimeOffset.UtcNow;
        var run = Run.Create(ProjectId.New(), createdAt);
        var taskId = Guid.CreateVersion7();
        var stampedAt = createdAt.AddSeconds(1);

        run.StampWorkBacklink(taskId, ordinal: 1, predecessorRunId: null, triggeringActorId: "scheduler/cron-3", now: stampedAt);
        var beforeTaskId = run.TaskId;
        var beforeOrdinal = run.AttemptOrdinal;
        var beforePredecessor = run.PredecessorRunId;
        var beforeActor = run.TriggeringActorId;
        var beforeUpdatedAt = run.UpdatedAt;

        var replayedAt = stampedAt.AddMinutes(5);
        run.StampWorkBacklink(taskId, ordinal: 1, predecessorRunId: Guid.CreateVersion7(), triggeringActorId: "scheduler/cron-3", now: replayedAt);

        run.TaskId.ShouldBe(beforeTaskId);
        run.AttemptOrdinal.ShouldBe(beforeOrdinal);
        run.PredecessorRunId.ShouldBe(beforePredecessor);
        run.TriggeringActorId.ShouldBe(beforeActor);
        run.UpdatedAt.ShouldBe(beforeUpdatedAt);
    }

    [Fact(DisplayName = "Given an already-stamped run, when StampWorkBacklink is called with a different taskId, then it throws with the work_backlink.mismatch code")]
    public void DifferentTaskIdThrows()
    {
        var createdAt = DateTimeOffset.UtcNow;
        var run = Run.Create(ProjectId.New(), createdAt);
        run.StampWorkBacklink(Guid.CreateVersion7(), ordinal: 1, predecessorRunId: null, triggeringActorId: "integrations/github-bot", now: createdAt.AddSeconds(1));

        var differentTaskId = Guid.CreateVersion7();

        var exception = Should.Throw<OrchestrationDomainException>(() =>
            run.StampWorkBacklink(differentTaskId, ordinal: 1, predecessorRunId: null, triggeringActorId: "integrations/github-bot", now: createdAt.AddSeconds(2)));

        exception.Code.ShouldBe(OrchestrationErrorCodes.RunWorkBacklinkMismatch);
    }

    [Fact(DisplayName = "Given an already-stamped run, when StampWorkBacklink is called with the same taskId but a different ordinal, then it throws with the work_backlink.mismatch code")]
    public void DifferentOrdinalThrows()
    {
        var createdAt = DateTimeOffset.UtcNow;
        var run = Run.Create(ProjectId.New(), createdAt);
        var taskId = Guid.CreateVersion7();
        run.StampWorkBacklink(taskId, ordinal: 1, predecessorRunId: null, triggeringActorId: "integrations/github-bot", now: createdAt.AddSeconds(1));

        var exception = Should.Throw<OrchestrationDomainException>(() =>
            run.StampWorkBacklink(taskId, ordinal: 2, predecessorRunId: Guid.CreateVersion7(), triggeringActorId: "integrations/github-bot", now: createdAt.AddSeconds(2)));

        exception.Code.ShouldBe(OrchestrationErrorCodes.RunWorkBacklinkMismatch);
    }

    [Fact(DisplayName = "Given an unstamped run, when StampWorkBacklink is called with an empty triggering actor id, then it throws with the work_backlink.mismatch code")]
    public void EmptyTriggeringActorThrows()
    {
        var createdAt = DateTimeOffset.UtcNow;
        var run = Run.Create(ProjectId.New(), createdAt);

        var exception = Should.Throw<OrchestrationDomainException>(() =>
            run.StampWorkBacklink(Guid.CreateVersion7(), ordinal: 1, predecessorRunId: null, triggeringActorId: "  ", now: createdAt.AddSeconds(1)));

        exception.Code.ShouldBe(OrchestrationErrorCodes.RunWorkBacklinkMismatch);
        run.TaskId.ShouldBeNull();
        run.TriggeringActorId.ShouldBeNull();
    }
}
