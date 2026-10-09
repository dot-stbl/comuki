using System.Text.Json;
using Comuki.Modules.Work.Application.Events;
using Comuki.Modules.Work.Application.Ports.Engine;
using Comuki.Modules.Work.Application.Ports.Inbox;
using Comuki.Modules.Work.Infrastructure.Inbox;
using Comuki.Modules.Work.Infrastructure.Subscribers;
using Comuki.Modules.Work.Unit.Fakes;
using Comuki.Shared.Bootstrap.Workers;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Work.Unit.Subscribers;

/// <summary>
/// Unit tests for <see cref="WorkAttemptCancelledSubscriber"/> —
/// the per-row seam that pulls <c>work.task.attempt-cancelled.v1</c>
/// rows off the engine outbox and forwards the cancelled Run id to
/// <see cref="Comuki.Modules.Work.Application.Ports.Engine.IWorkCancelPort"/>.
/// The fixture guards B1: the payload's <see cref="WorkAttemptCancelledEvent.RunId"/>
/// is the field the subscriber reads (the workTask's
/// <c>ActiveAttemptId</c> is cleared by the handler in the same
/// transaction, so the broader <see cref="WorkTaskEvent"/>
/// envelope would observe <c>null</c> at the publish site — every
/// row would skip as poison).
/// </summary>
public sealed class WorkAttemptCancelledSubscriberShould
{
    [Fact(DisplayName = "Given an AttemptCancelled row, when the cycle runs, then IWorkCancelPort.CancelAsync is called with the payload's RunId")]
    public async Task ForwardsRunIdToCancelPortAsync()
    {
        var runId = Guid.CreateVersion7();
        var row = SampleRow(new WorkAttemptCancelledEvent(
            TaskId: Guid.CreateVersion7(),
            ProjectId: Guid.CreateVersion7(),
            RunId: runId,
            BriefVersion: 1,
            OccurredAt: DateTimeOffset.UtcNow));

        var (subscriber, cancelPort, seedRows) = CreateSubscriber(row);

        var result = await subscriber.ExecuteAsync(NewContext(), TestContext.Current.CancellationToken);

        result.Success.ShouldBeTrue();
        result.Detail!.ShouldContain("work-attempt-cancelled-subscriber-read 1, processed 1, skipped 0");
        await cancelPort.Received(1).CancelAsync(
            new RunId(runId),
            "work.attempt-cancelled",
            Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a malformed AttemptCancelled row, when the cycle runs, then the row is skipped (no port call)")]
    public async Task MalformedPayloadSkippedAsync()
    {
        var row = new OrchestrationOutboxRow(
            Guid.NewGuid(),
            WorkTaskEventTypes.AttemptCancelled,
            Payload: "{ not valid json ");

        var (subscriber, cancelPort, seedRows) = CreateSubscriber(row);

        var result = await subscriber.ExecuteAsync(NewContext(), TestContext.Current.CancellationToken);

        result.Success.ShouldBeTrue();
        // The subscriber doesn't see a malformed row as a failure —
        // it poison-skips and advances the watermark so the cycle
        // doesn't loop.
        await cancelPort.DidNotReceiveWithAnyArgs().CancelAsync(default, default, TestContext.Current.CancellationToken);
    }

    private static WorkerContext NewContext()
    {
        return new WorkerContext(
            Services: new ServiceCollection().BuildServiceProvider(),
            Clock: TimeProvider.System,
            Logger: NullLogger.Instance);
    }

    private static OrchestrationOutboxRow SampleRow(WorkAttemptCancelledEvent payload)
    {
        return new OrchestrationOutboxRow(
            Id: Guid.NewGuid(),
            Type: WorkTaskEventTypes.AttemptCancelled,
            Payload: JsonSerializer.Serialize(payload, JsonSerializerOptions.Web));
    }

    private static (WorkAttemptCancelledSubscriber subscriber,
            IWorkCancelPort cancelPort,
            List<OrchestrationOutboxRow> seedRows) CreateSubscriber(OrchestrationOutboxRow seed)
    {
        var cancelPort = Substitute.For<IWorkCancelPort>();
        cancelPort.CancelAsync(Arg.Any<RunId>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var seedRows = new List<OrchestrationOutboxRow> { seed };
        var watermarks = new InMemoryWorkInboxWatermarkStore();
        var inbox = new InMemoryWorkInbox();

        var services = new ServiceCollection();
        services.AddSingleton<IWorkCancelPort>(cancelPort);
        services.AddSingleton<IWorkInboxWatermarkStore>(watermarks);
        services.AddSingleton<IWorkInbox>(inbox);
        // The reader returns whatever rows the test seeded ahead of
        // the cycle — a tight in-memory double stands in for the
        // host adapter's EF query.
        services.AddSingleton<IOrchestrationOutboxReader>(new TestOutboxReader(() => [.. seedRows]));
        var provider = services.BuildServiceProvider();

        var subscriber = new WorkAttemptCancelledSubscriber(
            new SingleScopeFactory(provider),
            TimeProvider.System,
            NullLogger<WorkAttemptCancelledSubscriber>.Instance);

        return (subscriber, cancelPort, seedRows);
    }

    private sealed class SingleScopeFactory(IServiceProvider root) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new NoopScope(root);
    }

    private sealed class NoopScope(IServiceProvider provider) : IServiceScope
    {
        public IServiceProvider ServiceProvider { get; } = provider;
        public void Dispose() { }
    }

    private sealed class TestOutboxReader(Func<IReadOnlyList<OrchestrationOutboxRow>> rows) : IOrchestrationOutboxReader
    {
        public Task<OrchestrationOutboxBatch> PollAsync(
            IReadOnlyCollection<string> types,
            Guid lastSeenId,
            CancellationToken cancellationToken = default)
        {
            var list = rows().Where(r => types.Contains(r.Type) && r.Id > lastSeenId).ToList();
            var highest = list.Count == 0 ? lastSeenId : list[^1].Id;
            return Task.FromResult(new OrchestrationOutboxBatch(list.Count, highest, list));
        }
    }
}
