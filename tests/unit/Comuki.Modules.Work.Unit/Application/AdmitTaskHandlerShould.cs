using Comuki.Modules.Work.Application.Admission;
using Comuki.Modules.Work.Domain;
using Comuki.Modules.Work.Domain.Exceptions;
using Comuki.Modules.Work.Domain.Sources;
using Comuki.Modules.Work.Unit.Fakes;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Work.Unit.Application;

/// <summary>
/// Application-handler T0 tests for <see cref="AdmitTaskHandler"/>:
/// first-admission path (a fresh Task + outbox event), idempotent
/// replay (same inbound id returns the same Task id and
/// republishes nothing), and the dispatch inbox claim — the
/// mirror of WS9's admission claim that the engine inbox uses
/// for the same pattern.
/// </summary>
public sealed class AdmitTaskHandlerShould
{
    [Fact(DisplayName = "Given a fresh inbound id, when AdmitTask is called, then a Task is created and Created is published")]
    public async Task FirstAdmissionCreatesTaskAndPublishesAsync()
    {
        var (handler, tasks, _, _, outbox) = TestHandlers.CreateAdmitTaskHandler();
        var project = TestFixtures.Project();
        var inboundId = "dot-stbl/comuki#89";
        var command = new AdmitTaskCommand(
            InboundItemExternalId: inboundId,
            ProjectId: project,
            Title: "Wire the inbound webhook to WorkTask",
            Brief: "Admit a tracker ticket and dispatch a Run.",
            SourceKind: WorkTaskSourceKind.GitHub,
            SourceDisplayName: "comuki#89");

        var outcome = await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        outcome.WasReplay.ShouldBeFalse();
        var task = await tasks.FindAsync(outcome.TaskId, TestContext.Current.CancellationToken);
        task.ShouldNotBeNull();
        task!.ProjectId.ShouldBe(project);
        task.Status.ShouldBe(WorkTaskStatus.Draft);
        task.SourceRefs[0].ExternalId.ShouldBe(inboundId);
        var (eventType, payload) = OutboxSpy.LastCall(outbox);
        eventType.ShouldBe("work.task.created.v1");
        var published = payload.ShouldBeOfType<AdmitTaskHandler.AdmittedEvent>();
        published.TaskId.ShouldBe(outcome.TaskId.Value);
        published.InboundItemExternalId.ShouldBe(inboundId);
    }

    [Fact(DisplayName = "Given the same inbound id twice, when AdmitTask is called twice, then the second call returns the same Task id and does not republish")]
    public async Task ReplayReturnsSameTaskIdAndDoesNotRepublishAsync()
    {
        var (handler, tasks, _, _, outbox) = TestHandlers.CreateAdmitTaskHandler();
        var project = TestFixtures.Project();
        var inboundId = "dot-stbl/comuki#89";
        var command = new AdmitTaskCommand(inboundId, project, "title", "brief", WorkTaskSourceKind.GitHub, "comuki#89");

        var first = await handler.HandleAsync(command, TestContext.Current.CancellationToken);
        var second = await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        second.WasReplay.ShouldBeTrue();
        second.TaskId.ShouldBe(first.TaskId);
        var task = await tasks.FindAsync(first.TaskId, TestContext.Current.CancellationToken);
        task.ShouldNotBeNull();
        // Only one publish on the first call.
        await outbox.Received(1).PublishAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given different inbound ids, when AdmitTask is called, then two Tasks are created and two Created events are published")]
    public async Task DistinctInboundIdsCreateDistinctTasksAsync()
    {
        var (handler, tasks, _, _, outbox) = TestHandlers.CreateAdmitTaskHandler();
        var project = TestFixtures.Project();
        var first = await handler.HandleAsync(new AdmitTaskCommand("a#1", project, "title-a", "brief-a", WorkTaskSourceKind.GitHub, "a#1"), TestContext.Current.CancellationToken);
        var second = await handler.HandleAsync(new AdmitTaskCommand("a#2", project, "title-b", "brief-b", WorkTaskSourceKind.GitHub, "a#2"), TestContext.Current.CancellationToken);

        first.TaskId.ShouldNotBe(second.TaskId);
        var firstTask = await tasks.FindAsync(first.TaskId, TestContext.Current.CancellationToken);
        var secondTask = await tasks.FindAsync(second.TaskId, TestContext.Current.CancellationToken);
        firstTask.ShouldNotBeNull();
        secondTask.ShouldNotBeNull();
        await outbox.Received(2).PublishAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given an empty title, when AdmitTask is called, then the aggregate throws and no Task is created")]
    public async Task EmptyTitleIsRejectedByAggregateAsync()
    {
        var (handler, _, _, _, outbox) = TestHandlers.CreateAdmitTaskHandler();
        var project = TestFixtures.Project();

        await Should.ThrowAsync<WorkTaskDomainException>(
            async () => await handler.HandleAsync(new AdmitTaskCommand("a#1", project, " ", "brief", WorkTaskSourceKind.GitHub, "a#1"), TestContext.Current.CancellationToken));

        await outbox.DidNotReceiveWithAnyArgs().PublishAsync(default!, default!, TestContext.Current.CancellationToken);
    }
}

/// <summary>
/// Watermark-store T0 tests — the per-type Guid
/// monotonic-direction contract. The store is the seam that
/// lets the subscribers resume from the last-seen id across a
/// host restart; the contract is "smaller never replaces
/// larger", which is what <see cref="InMemoryWorkInboxWatermarkStore.RecordAsync(string, Guid, CancellationToken)"/>
/// enforces.
/// </summary>
public sealed class WorkInboxWatermarkStoreShould
{
    private static readonly Guid sampleId = new("00000000-0000-0000-0000-000000000042");
    private static readonly Guid biggerId = new("00000000-0000-0000-0000-000000000064");
    private static readonly Guid smallerId = new("00000000-0000-0000-0000-000000000032");

    [Fact(DisplayName = "Given an unseen type, when Get is called, then it returns Guid.Empty")]
    public async Task GetReturnsEmptyForUnseenTypeAsync()
    {
        var store = new InMemoryWorkInboxWatermarkStore();

        (await store.GetAsync("orchestration.run.terminated.v1", TestContext.Current.CancellationToken)).ShouldBe(Guid.Empty);
    }

    [Fact(DisplayName = "Given a recorded id, when Get is called, then it returns the recorded id")]
    public async Task GetReturnsRecordedIdAsync()
    {
        var store = new InMemoryWorkInboxWatermarkStore();
        await store.RecordAsync("orchestration.run.terminated.v1", sampleId, TestContext.Current.CancellationToken);

        (await store.GetAsync("orchestration.run.terminated.v1", TestContext.Current.CancellationToken)).ShouldBe(sampleId);
    }

    [Fact(DisplayName = "Given a smaller id after a larger one, when Record is called, then Get still returns the larger id")]
    public async Task RecordIsMonotonicAsync()
    {
        var store = new InMemoryWorkInboxWatermarkStore();
        await store.RecordAsync("orchestration.run.terminated.v1", biggerId, TestContext.Current.CancellationToken);
        await store.RecordAsync("orchestration.run.terminated.v1", smallerId, TestContext.Current.CancellationToken);

        (await store.GetAsync("orchestration.run.terminated.v1", TestContext.Current.CancellationToken)).ShouldBe(biggerId);
    }

    [Fact(DisplayName = "Given two distinct types, when Record is called, then each Get returns its own value")]
    public async Task WatermarksAreIsolatedByTypeAsync()
    {
        var store = new InMemoryWorkInboxWatermarkStore();
        await store.RecordAsync("orchestration.run.terminated.v1", biggerId, TestContext.Current.CancellationToken);
        await store.RecordAsync("orchestration.run.cancelled.v1", smallerId, TestContext.Current.CancellationToken);

        (await store.GetAsync("orchestration.run.terminated.v1", TestContext.Current.CancellationToken)).ShouldBe(biggerId);
        (await store.GetAsync("orchestration.run.cancelled.v1", TestContext.Current.CancellationToken)).ShouldBe(smallerId);
    }
}

/// <summary>
/// Work-inbox T0 tests — the dedupe-ledger mirror on the Work
/// side. The interface is what <see cref="AdmitTaskHandler"/>
/// and the engine-side terminal-event consumer both call.
/// </summary>
public sealed class WorkInboxShould
{
    [Fact(DisplayName = "Given a fresh id, when TryClaim is called, then it returns true")]
    public async Task FirstClaimReturnsTrueAsync()
    {
        var inbox = new InMemoryWorkInbox();

        (await inbox.TryClaimAsync(AdmitTaskHandler.InboxMessageId("a#1"), TestContext.Current.CancellationToken)).ShouldBeTrue();
    }

    [Fact(DisplayName = "Given the same id twice, when TryClaim is called twice, then the second call returns false")]
    public async Task SecondClaimReturnsFalseAsync()
    {
        var inbox = new InMemoryWorkInbox();
        var id = AdmitTaskHandler.InboxMessageId("a#1");

        (await inbox.TryClaimAsync(id, TestContext.Current.CancellationToken)).ShouldBeTrue();
        (await inbox.TryClaimAsync(id, TestContext.Current.CancellationToken)).ShouldBeFalse();
    }

    [Fact(DisplayName = "Given an empty id, when TryClaim is called, then the claim still returns a value (validation lives in the EF implementation, not in the contract)")]
    public async Task EmptyIdAcceptedByInMemoryInboxAsync()
    {
        var inbox = new InMemoryWorkInbox();

        var first = await inbox.TryClaimAsync(" ", TestContext.Current.CancellationToken);
        var second = await inbox.TryClaimAsync(" ", TestContext.Current.CancellationToken);

        first.ShouldBeTrue();
        second.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given the dedupe key shape, when built, then the prefix scopes the message id to the Work context")]
    public void DedupeKeyShape()
    {
        AdmitTaskHandler.InboxMessageId("a#1").ShouldBe("work.inbox.admit.a#1");
    }
}
