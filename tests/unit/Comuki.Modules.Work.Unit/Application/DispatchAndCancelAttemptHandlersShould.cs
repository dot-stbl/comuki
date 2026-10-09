using Comuki.Modules.Work.Application.Admission;
using Comuki.Modules.Work.Application.Cancellation;
using Comuki.Modules.Work.Application.Dispatch;
using Comuki.Modules.Work.Domain;
using Comuki.Modules.Work.Domain.Exceptions;
using Comuki.Modules.Work.Domain.Sources;
using Comuki.Shared.Contracts;
using Comuki.Shared.Kernel.Ids;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Work.Unit.Application;

/// <summary>
/// Application-handler T0 tests for
/// <see cref="DispatchRunHandler"/>
/// and
/// <see cref="CancelAttemptHandler"/>:
/// the one-active-Run guard at the Application layer, the
/// idempotency seam on the same run id, and the AttemptRequested
/// event publication. The tests exercise the aggregate's
/// <c>AppendAttempt</c> / <c>CompleteAttempt</c> guards
/// end-to-end through the handlers.
/// </summary>
public sealed class DispatchAndCancelAttemptHandlersShould
{
    [Fact(DisplayName = "Given a Ready Task, when DispatchRun is called, then the active attempt is set and AttemptRequested is published")]
    public async Task DispatchRunActivatesAttemptAsync()
    {
        var (admit, tasks, _) = await AdmitAndReadyAsync();
        var (handler, dispatchOutbox) = TestHandlers.CreateDispatchRunHandler(tasks);
        var runId = RunId.New();
        var dispatch = TestFixtures.Dispatch(admit.TaskId, attemptOrdinal: 1);

        var outcome = await handler.HandleAsync(new DispatchRunCommand(admit.TaskId, runId, dispatch), TestContext.Current.CancellationToken);

        outcome.AttemptOrdinal.ShouldBe(1);
        outcome.WasReplay.ShouldBeFalse();
        var task = await tasks.FindAsync(admit.TaskId, TestContext.Current.CancellationToken);
        task.ShouldNotBeNull();
        task!.ActiveAttemptId.ShouldBe(runId);
        task.AttemptOrdinal.Value.ShouldBe(1);
        await dispatchOutbox.Received(1).PublishAsync("work.task.attempt-requested.v1", Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a Task with an active attempt, when DispatchRun with a different run id is called, then the aggregate throws")]
    public async Task DispatchRunRejectsConcurrentDispatchAsync()
    {
        var (admit, tasks, _) = await AdmitAndReadyAsync();
        var (handler, _) = TestHandlers.CreateDispatchRunHandler(tasks);
        var dispatch = TestFixtures.Dispatch(admit.TaskId, attemptOrdinal: 1);
        await handler.HandleAsync(new DispatchRunCommand(admit.TaskId, RunId.New(), dispatch), TestContext.Current.CancellationToken);

        await Should.ThrowAsync<WorkTaskDomainException>(
            async () => await handler.HandleAsync(new DispatchRunCommand(admit.TaskId, RunId.New(), dispatch), TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "Given a Task with an active attempt, when DispatchRun with the SAME run id is called, then the outcome is a no-op replay")]
    public async Task DispatchRunIsIdempotentOnSameRunIdAsync()
    {
        var (admit, tasks, _) = await AdmitAndReadyAsync();
        var (handler, outbox) = TestHandlers.CreateDispatchRunHandler(tasks);
        var runId = RunId.New();
        var dispatch = TestFixtures.Dispatch(admit.TaskId, attemptOrdinal: 1);

        await handler.HandleAsync(new DispatchRunCommand(admit.TaskId, runId, dispatch), TestContext.Current.CancellationToken);
        var replay = await handler.HandleAsync(new DispatchRunCommand(admit.TaskId, runId, dispatch), TestContext.Current.CancellationToken);

        replay.WasReplay.ShouldBeTrue();
        // Only one publish — the no-op replay must not emit a
        // second attempt-requested event.
        await outbox.Received(1).PublishAsync("work.task.attempt-requested.v1", Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a Task without a Ready / Blocked status, when DispatchRun is called, then the aggregate throws")]
    public async Task DispatchRunRejectsDraftAsync()
    {
        var (admit, tasks, _) = await AdmitDraftAsync();
        var (handler, _) = TestHandlers.CreateDispatchRunHandler(tasks);
        var dispatch = TestFixtures.Dispatch(admit.TaskId, attemptOrdinal: 1);

        await Should.ThrowAsync<WorkTaskDomainException>(
            async () => await handler.HandleAsync(new DispatchRunCommand(admit.TaskId, RunId.New(), dispatch), TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "Given a Task with an active attempt, when CancelAttempt is called with the matching run id, then the attempt is cleared and AttemptCancelled is published with the cancelled RunId on the payload")]
    public async Task CancelAttemptClearsActiveAttemptAsync()
    {
        var (admit, tasks, _) = await AdmitAndReadyAsync();
        var (dispatchHandler, _) = TestHandlers.CreateDispatchRunHandler(tasks);
        var (cancelHandler, cancelOutbox) = TestHandlers.CreateCancelAttemptHandler(tasks);
        var runId = RunId.New();
        await dispatchHandler.HandleAsync(new DispatchRunCommand(
            admit.TaskId, runId, TestFixtures.Dispatch(admit.TaskId, attemptOrdinal: 1)), TestContext.Current.CancellationToken);

        await cancelHandler.HandleAsync(new CancelAttemptCommand(admit.TaskId, runId), TestContext.Current.CancellationToken);

        var task = await tasks.FindAsync(admit.TaskId, TestContext.Current.CancellationToken);
        task.ShouldNotBeNull();
        task!.ActiveAttemptId.ShouldBeNull();
        // The payload now carries the cancelled RunId explicitly
        // (B1 fix) — the subscriber used to read it from
        // task.ActiveAttemptId before the CompleteAttempt cleared
        // it, observing Guid.Empty on every row and skipping them
        // as poison; the dedicated <c>work.task.attempt-cancelled.v1</c>
        // envelope surfaces the run id the engine needs to forward.
        await cancelOutbox.Received().PublishAsync(
            Comuki.Modules.Work.Application.Events.WorkTaskEventTypes.AttemptCancelled,
            Arg.Is<Comuki.Modules.Work.Application.Events.WorkAttemptCancelledEvent>(
                payload => payload.TaskId == admit.TaskId.Value
                    && payload.ProjectId == task.ProjectId.Value
                    && payload.RunId == runId.Value),
            Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a Task with an active attempt, when CancelAttempt is called with a mismatching run id, then the aggregate throws")]
    public async Task CancelAttemptRejectsMismatchedRunIdAsync()
    {
        var (admit, tasks, _) = await AdmitAndReadyAsync();
        var (dispatchHandler, _) = TestHandlers.CreateDispatchRunHandler(tasks);
        var (cancelHandler, _) = TestHandlers.CreateCancelAttemptHandler(tasks);
        await dispatchHandler.HandleAsync(new DispatchRunCommand(
            admit.TaskId, RunId.New(), TestFixtures.Dispatch(admit.TaskId, attemptOrdinal: 1)), TestContext.Current.CancellationToken);

        await Should.ThrowAsync<WorkTaskDomainException>(
            async () => await cancelHandler.HandleAsync(new CancelAttemptCommand(admit.TaskId, RunId.New()), TestContext.Current.CancellationToken));
    }

    private static async Task<(AdmitTaskOutcome Outcome, InMemoryWorkTaskStore Tasks, IOutbox Outbox)> AdmitDraftAsync()
    {
        var (handler, tasks, _, _, outbox) = TestHandlers.CreateAdmitTaskHandler();
        var outcome = await handler.HandleAsync(new AdmitTaskCommand(
            InboundItemExternalId: "dot-stbl/comuki#89",
            ProjectId: TestFixtures.Project(),
            Title: "Wire the inbound webhook to WorkTask",
            Brief: "Admit a tracker ticket and dispatch a Run.",
            SourceKind: WorkTaskSourceKind.GitHub,
            SourceDisplayName: "comuki#89"),
            TestContext.Current.CancellationToken);
        return (outcome, tasks, outbox);
    }

    private static async Task<(AdmitTaskOutcome Outcome, InMemoryWorkTaskStore Tasks, IOutbox Outbox)> AdmitAndReadyAsync()
    {
        var (outcome, tasks, outbox) = await AdmitDraftAsync();
        var task = await tasks.FindAsync(outcome.TaskId, TestContext.Current.CancellationToken);
        task.ShouldNotBeNull();
        task.TransitionTo(WorkTaskStatus.Ready, null, DateTimeOffset.UtcNow);
        await tasks.SaveAsync(task, TestContext.Current.CancellationToken);
        return (outcome, tasks, outbox);
    }
}
